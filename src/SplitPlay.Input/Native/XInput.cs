using System.Runtime.InteropServices;

namespace SplitPlay.Input.Native;

/// <summary>
/// Thin P/Invoke wrapper around the parts of XInput we use: reading controller
/// state (connection + buttons/sticks/triggers) and setting the rumble motors.
/// </summary>
internal static class XInput
{
    /// <summary>XInput supports a maximum of four controllers (indices 0-3).</summary>
    public const int MaxControllers = 4;

    public const int ErrorSuccess = 0;

    // xinput1_4.dll ships with Windows 8+. On the supported OS range it is always
    // present, so we bind to it directly.
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    public static extern int XInputGetState(int dwUserIndex, out XINPUT_STATE pState);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
    public static extern int XInputSetState(int dwUserIndex, ref XINPUT_VIBRATION pVibration);

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_VIBRATION
    {
        public ushort wLeftMotorSpeed;
        public ushort wRightMotorSpeed;
    }
}
