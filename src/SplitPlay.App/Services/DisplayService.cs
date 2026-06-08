using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SplitPlay.Core.Abstractions;
using SplitPlay.Core.Models;

namespace SplitPlay.App.Services;

/// <summary>
/// <see cref="IDisplayService"/> implemented with the Win32 monitor-enumeration
/// API (EnumDisplayMonitors + GetMonitorInfo). It reports device-pixel bounds,
/// which is exactly what the layout calculator and window manager need. Using
/// Win32 directly keeps the app a pure WPF project (no WinForms reference, and
/// therefore no Application/UserControl namespace ambiguities).
/// </summary>
public sealed class DisplayService : IDisplayService
{
    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var displays = new List<DisplayInfo>();

        bool Callback(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data)
        {
            var info = new MONITORINFOEX();
            info.cbSize = Marshal.SizeOf<MONITORINFOEX>();
            if (GetMonitorInfo(hMonitor, ref info))
            {
                bool primary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0;
                displays.Add(new DisplayInfo
                {
                    DeviceName = info.szDevice,
                    IsPrimary = primary,
                    Bounds = ToRegion(info.rcMonitor),
                    WorkingArea = ToRegion(info.rcWork)
                });
            }

            return true; // keep enumerating
        }

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);

        // Primary first so index 0 is always the primary monitor.
        displays.Sort((a, b) => b.IsPrimary.CompareTo(a.IsPrimary));

        // Defensive fallback: if enumeration somehow returned nothing, synthesize
        // a single display from the primary screen metrics so the UI still works.
        if (displays.Count == 0)
        {
            int w = GetSystemMetrics(SM_CXSCREEN);
            int h = GetSystemMetrics(SM_CYSCREEN);
            displays.Add(new DisplayInfo
            {
                DeviceName = @"\\.\DISPLAY1",
                IsPrimary = true,
                Bounds = new ScreenRegion(0, 0, w, h),
                WorkingArea = new ScreenRegion(0, 0, w, h)
            });
        }

        return displays;
    }

    public DisplayInfo GetPrimaryDisplay()
    {
        IReadOnlyList<DisplayInfo> all = GetDisplays();
        foreach (DisplayInfo d in all)
        {
            if (d.IsPrimary)
            {
                return d;
            }
        }

        return all[0];
    }

    private static ScreenRegion ToRegion(RECT r) =>
        new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    // ----------------------------- Win32 interop -----------------------------

    private const int MONITORINFOF_PRIMARY = 0x1;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
