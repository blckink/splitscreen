// SplitPlay XInput proxy
// =======================
//
// A drop-in replacement for xinput1_3.dll / xinput1_4.dll / xinput9_1_0.dll that
// makes a game instance see ONLY one physical controller, presented as user
// index 0. Every other index is reported as "not connected". This is how we
// restrict one pad to one game window:
//
//   - The launcher copies this DLL (under the three xinput names) next to the
//     game executable. Because Windows searches the application directory before
//     System32, the game loads THIS dll instead of the real one.
//   - The launcher starts each instance with the environment variable
//     SPLITPLAY_XINPUT_INDEX set to the physical controller index that instance
//     should use. This dll reads it and forwards only that pad as index 0.
//   - All real work is forwarded to the genuine xinput1_4.dll in System32, which
//     we load by full path so we never recurse into ourselves.
//
// Keyboard and mouse are never touched, so the desktop stays fully usable.

#include <windows.h>
#include <Xinput.h>
#include <tlhelp32.h>
#include <stdlib.h>
#include <stdio.h>
#include <string.h>
#include "MinHook.h"

// We call user32 window APIs (to pin the game window to its split region from
// inside the process), so we must link user32.lib. Kernel32 is linked by default.
#pragma comment(lib, "user32.lib")

// QWORD is referenced by the NEXTRAWINPUTBLOCK macro but is not defined by the
// SDK headers we include, so define it ourselves.
#ifndef QWORD
typedef unsigned __int64 QWORD;
#endif

// XInputGetStateEx (hidden ordinal 100) exposes the Guide button. Many games
// import it by ordinal; we mirror it.
typedef DWORD(WINAPI* PFN_GetState)(DWORD, XINPUT_STATE*);
typedef DWORD(WINAPI* PFN_SetState)(DWORD, XINPUT_VIBRATION*);
typedef DWORD(WINAPI* PFN_GetCapabilities)(DWORD, DWORD, XINPUT_CAPABILITIES*);
typedef void (WINAPI* PFN_Enable)(BOOL);
typedef DWORD(WINAPI* PFN_GetBattery)(DWORD, BYTE, XINPUT_BATTERY_INFORMATION*);
typedef DWORD(WINAPI* PFN_GetKeystroke)(DWORD, DWORD, PXINPUT_KEYSTROKE);
typedef DWORD(WINAPI* PFN_GetDSoundGuids)(DWORD, GUID*, GUID*);

extern "C" DWORD WINAPI XInputGetState(DWORD dwUserIndex, XINPUT_STATE* pState);
extern "C" DWORD WINAPI XInputGetStateEx(DWORD dwUserIndex, XINPUT_STATE* pState);

static volatile LONG g_initState = 0;       // 0 = uninit, 1 = initializing, 2 = ready
static DWORD         g_assignedIndex = 0;    // physical pad exposed as index 0
static HMODULE       g_real = NULL;

static PFN_GetState         p_GetState = NULL;
static PFN_GetState         p_GetStateEx = NULL;   // ordinal 100
static PFN_SetState         p_SetState = NULL;
static PFN_GetCapabilities  p_GetCapabilities = NULL;
static PFN_Enable           p_Enable = NULL;
static PFN_GetBattery       p_GetBattery = NULL;
static PFN_GetKeystroke     p_GetKeystroke = NULL;
static PFN_GetDSoundGuids   p_GetDSoundGuids = NULL;

// Live controller slot: the launcher can rewrite which physical XInput slot this
// instance follows (e.g. a pad was switched off and came back on another slot).
static char      g_padFile[MAX_PATH] = { 0 };
static ULONGLONG g_lastPadCheck = 0;

// Window enforcement target: the split region this instance must stay pinned to.
static int     g_winX = 0, g_winY = 0, g_winW = 0, g_winH = 0;
static bool    g_hasWindowTarget = false;
static HWND    g_hookedWnd = NULL;
static WNDPROC g_origProc = NULL;

static void AssignRawDevice();
static void InstallRawInputHooks();

static DWORD WINAPI WindowEnforceThread(LPVOID);
static void RefreshAssignedIndex();

// Diagnostic log written next to the proxy DLL (splitplay_proxy.log). Lets us see
// whether the proxy is loaded at all, which controller index it took, and - crucially
// - whether the game actually calls XInput (vs reading pads via another API).
static void ProxyLog(const char* msg)
{
    static char logPath[MAX_PATH] = { 0 };
    if (logPath[0] == 0)
    {
        HMODULE self = NULL;
        if (!GetModuleHandleExA(
                GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                (LPCSTR)(void*)&ProxyLog, &self))
        {
            return;
        }
        char modPath[MAX_PATH] = { 0 };
        GetModuleFileNameA(self, modPath, MAX_PATH);
        char* slash = strrchr(modPath, '\\');
        if (slash != NULL)
        {
            *slash = 0;
        }
        sprintf_s(logPath, MAX_PATH, "%s\\splitplay_proxy.log", modPath);
    }

    HANDLE hf = CreateFileA(logPath, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
                            NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (hf == INVALID_HANDLE_VALUE)
    {
        return;
    }
    char line[600];
    int len = sprintf_s(line, sizeof(line), "[pid %lu] %s\r\n", GetCurrentProcessId(), msg);
    if (len > 0)
    {
        DWORD written = 0;
        WriteFile(hf, line, (DWORD)len, &written, NULL);
    }
    CloseHandle(hf);
}

// Lazily initialises the proxy on first use. We deliberately avoid doing this in
// DllMain to stay clear of loader-lock restrictions on LoadLibrary.
static void EnsureInit()
{
    if (g_initState == 2)
    {
        return;
    }

    // Only the first caller performs initialisation; others spin until ready.
    if (InterlockedCompareExchange(&g_initState, 1, 0) != 0)
    {
        while (g_initState != 2)
        {
            Sleep(0);
        }
        return;
    }

    // Read which physical controller this instance should expose.
    char buffer[16] = { 0 };
    DWORD len = GetEnvironmentVariableA("SPLITPLAY_XINPUT_INDEX", buffer, sizeof(buffer));
    if (len > 0 && len < sizeof(buffer))
    {
        int parsed = atoi(buffer);
        if (parsed >= 0 && parsed <= 3)
        {
            g_assignedIndex = (DWORD)parsed;
        }
    }

    // Optional: a file the launcher updates with the live XInput slot to follow.
    GetEnvironmentVariableA("SPLITPLAY_PAD_FILE", g_padFile, sizeof(g_padFile));

    // Optional: the split region this instance's window must stay pinned to.
    {
        char b[16] = { 0 };
        if (GetEnvironmentVariableA("SPLITPLAY_WIN_W", b, sizeof(b)) > 0)
        {
            g_winW = atoi(b);
            GetEnvironmentVariableA("SPLITPLAY_WIN_H", b, sizeof(b)); g_winH = atoi(b);
            GetEnvironmentVariableA("SPLITPLAY_WIN_X", b, sizeof(b)); g_winX = atoi(b);
            GetEnvironmentVariableA("SPLITPLAY_WIN_Y", b, sizeof(b)); g_winY = atoi(b);
            g_hasWindowTarget = (g_winW > 0 && g_winH > 0);
        }
    }

    // Load the genuine implementation by full system path (never by bare name, or
    // we would load ourselves from the application directory).
    char systemDir[MAX_PATH] = { 0 };
    UINT n = GetSystemDirectoryA(systemDir, MAX_PATH);
    if (n > 0 && n < MAX_PATH)
    {
        char path[MAX_PATH] = { 0 };
        // sprintf_s is part of the (statically linked) CRT, so we avoid a
        // dependency on user32.lib that wsprintfA would otherwise require.
        sprintf_s(path, MAX_PATH, "%s\\xinput1_4.dll", systemDir);
        g_real = LoadLibraryA(path);
    }

    if (g_real != NULL)
    {
        p_GetState = (PFN_GetState)GetProcAddress(g_real, "XInputGetState");
        p_GetStateEx = (PFN_GetState)GetProcAddress(g_real, (LPCSTR)MAKEINTRESOURCE(100));
        p_SetState = (PFN_SetState)GetProcAddress(g_real, "XInputSetState");
        p_GetCapabilities = (PFN_GetCapabilities)GetProcAddress(g_real, "XInputGetCapabilities");
        p_Enable = (PFN_Enable)GetProcAddress(g_real, "XInputEnable");
        p_GetBattery = (PFN_GetBattery)GetProcAddress(g_real, "XInputGetBatteryInformation");
        p_GetKeystroke = (PFN_GetKeystroke)GetProcAddress(g_real, "XInputGetKeystroke");
        p_GetDSoundGuids = (PFN_GetDSoundGuids)GetProcAddress(g_real, "XInputGetDSoundAudioDeviceGuids");
    }

    // MinHook the genuine XInput module. Unity uses GetProcAddress("xinput1_4.dll", ...)
    // internally. If it finds it and caches the real function pointers, it completely bypasses
    // our proxy's exported functions. So we must inline hook them inside the loaded system dll.
    MH_Initialize();
    if (g_real != NULL)
    {
        void* pGet = (void*)GetProcAddress(g_real, "XInputGetState");
        if (pGet) { MH_CreateHook(pGet, (void*)XInputGetState, (void**)&p_GetState); MH_EnableHook(pGet); }
        void* pGetEx = (void*)GetProcAddress(g_real, (LPCSTR)MAKEINTRESOURCE(100));
        if (pGetEx) { MH_CreateHook(pGetEx, (void*)XInputGetStateEx, (void**)&p_GetStateEx); MH_EnableHook(pGetEx); }
    }

    char init[300];
    sprintf_s(init, sizeof(init),
              "proxy loaded: assignedIndex=%lu padFile='%s' window=%dx%d@%d,%d realXInput=%s",
              g_assignedIndex, g_padFile[0] ? g_padFile : "(none)",
              g_winW, g_winH, g_winX, g_winY, p_GetState ? "yes" : "NO");
    ProxyLog(init);

    // Claim our gamepad, then install the inline Raw Input hooks so the game only
    // ever sees this instance's controller (the WM_INPUT filter is a fallback).
    // We do this immediately so games reading input early are caught.
    AssignRawDevice();
    InstallRawInputHooks();

    // If the launcher gave us a region, keep the game window inside it from
    // within the game process itself (clamps WM_WINDOWPOSCHANGING).
    if (g_hasWindowTarget)
    {
        HANDLE th = CreateThread(NULL, 0, WindowEnforceThread, NULL, 0, NULL);
        if (th != NULL)
        {
            CloseHandle(th);
        }
    }

    InterlockedExchange(&g_initState, 2);
}

// Re-reads the live slot file at most twice a second, off the input hot path.
static void RefreshAssignedIndex()
{
    if (g_padFile[0] == 0)
    {
        return;
    }
    ULONGLONG now = GetTickCount64();
    if (now - g_lastPadCheck < 500)
    {
        return;
    }
    g_lastPadCheck = now;

    HANDLE hf = CreateFileA(g_padFile, GENERIC_READ,
                            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                            NULL, OPEN_EXISTING, 0, NULL);
    if (hf == INVALID_HANDLE_VALUE)
    {
        return;
    }
    char buf[16] = { 0 };
    DWORD rd = 0;
    if (ReadFile(hf, buf, sizeof(buf) - 1, &rd, NULL) && rd > 0)
    {
        int parsed = atoi(buf);
        if (parsed >= 0 && parsed <= 3)
        {
            g_assignedIndex = (DWORD)parsed;
        }
    }
    CloseHandle(hf);
}

// ---- Resolution confinement (IAT hooks) --------------------------------
// The game switches itself to fullscreen a few seconds in and renders at the
// monitor resolution, which then gets squashed into our half-width split window.
// We make the game believe the monitor IS exactly our split region by patching a
// few display-query functions in the import tables of the game modules. Its
// "fullscreen" then targets the split size = our window, so the render target
// matches and nothing is stretched. This is the same trick borderless/split tools
// use; it touches only this process and never changes the real desktop.

typedef int  (WINAPI* PFN_GetSystemMetrics)(int);
typedef BOOL (WINAPI* PFN_GetMonitorInfoA)(HMONITOR, LPMONITORINFO);
typedef BOOL (WINAPI* PFN_GetMonitorInfoW)(HMONITOR, LPMONITORINFO);
typedef BOOL (WINAPI* PFN_EnumDisplaySettingsA)(LPCSTR, DWORD, DEVMODEA*);
typedef BOOL (WINAPI* PFN_EnumDisplaySettingsW)(LPCWSTR, DWORD, DEVMODEW*);
typedef LONG (WINAPI* PFN_ChangeDisplaySettingsExW)(LPCWSTR, DEVMODEW*, HWND, DWORD, LPVOID);

static PFN_GetSystemMetrics        o_GetSystemMetrics = NULL;
static PFN_GetMonitorInfoA         o_GetMonitorInfoA = NULL;
static PFN_GetMonitorInfoW         o_GetMonitorInfoW = NULL;
static PFN_EnumDisplaySettingsA    o_EnumDisplaySettingsA = NULL;
static PFN_EnumDisplaySettingsW    o_EnumDisplaySettingsW = NULL;
static PFN_ChangeDisplaySettingsExW o_ChangeDisplaySettingsExW = NULL;

// Raw Input API hooks (the real isolation: the game reads pads by calling these,
// not just via WM_INPUT). Defined further down; forward-declared so the IAT
// patcher can install them.
typedef UINT (WINAPI* PFN_GetRawInputData)(HRAWINPUT, UINT, LPVOID, PUINT, UINT);
typedef UINT (WINAPI* PFN_GetRawInputBuffer)(PRAWINPUT, PUINT, UINT);
static PFN_GetRawInputData   o_GetRawInputData = NULL;
static PFN_GetRawInputBuffer o_GetRawInputBuffer = NULL;
static UINT WINAPI My_GetRawInputData(HRAWINPUT, UINT, LPVOID, PUINT, UINT);
static UINT WINAPI My_GetRawInputBuffer(PRAWINPUT, PUINT, UINT);
static bool ShouldSwallowRawInput(HRAWINPUT hRawInput);
static bool IsGamepadDevice(HANDLE device);

// Message-pump hooks: the reliable way (used by Nucleus/ProtoInput) to drop a
// foreign gamepad is to blank its WM_INPUT message to WM_NULL before the game's
// own message loop processes it. Rewired (which Roots of Pacha uses) reads raw
// input on its own message loop, so failing GetRawInputData alone is not enough.
typedef BOOL (WINAPI* PFN_GetMessageW)(LPMSG, HWND, UINT, UINT);
typedef BOOL (WINAPI* PFN_PeekMessageW)(LPMSG, HWND, UINT, UINT, UINT);
static PFN_GetMessageW  o_GetMessageW = NULL;
static PFN_GetMessageW  o_GetMessageA = NULL;
static PFN_PeekMessageW o_PeekMessageW = NULL;
static PFN_PeekMessageW o_PeekMessageA = NULL;
static BOOL WINAPI My_GetMessageW(LPMSG, HWND, UINT, UINT);
static BOOL WINAPI My_GetMessageA(LPMSG, HWND, UINT, UINT);
static BOOL WINAPI My_PeekMessageW(LPMSG, HWND, UINT, UINT, UINT);
static BOOL WINAPI My_PeekMessageA(LPMSG, HWND, UINT, UINT, UINT);

// Direct-HID block: Rewired (and similar) can open the gamepad's HID device
// directly via CreateFile and read its reports, bypassing both XInput and the
// Raw Input API. We block opening any FOREIGN gamepad device so an instance can
// physically only read its own controller - the last in-process channel.
typedef HANDLE (WINAPI* PFN_CreateFileW)(LPCWSTR, DWORD, DWORD, LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
typedef HANDLE (WINAPI* PFN_CreateFileA)(LPCSTR, DWORD, DWORD, LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
static PFN_CreateFileW o_CreateFileW = NULL;
static PFN_CreateFileA o_CreateFileA = NULL;
static HANDLE WINAPI My_CreateFileW(LPCWSTR, DWORD, DWORD, LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
static HANDLE WINAPI My_CreateFileA(LPCSTR, DWORD, DWORD, LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
static char g_foreignKeys[8][200];
static int  g_foreignKeyCount = 0;

// Windows Gaming Input (WGI) block: Rewired/Unity can use WGI which completely
// bypasses XInput and Raw Input. We hook RoGetActivationFactory and block any
// requests for classes under Windows.Gaming.Input by returning REGDB_E_CLASSNOTREG.
// We use void* for HSTRING and REFIID to avoid including WinRT headers.
typedef HRESULT (WINAPI* PFN_RoGetActivationFactory)(void*, void*, void**);
typedef PCWSTR  (WINAPI* PFN_WindowsGetStringRawBuffer)(void*, UINT32*);
static PFN_RoGetActivationFactory o_RoGetActivationFactory = NULL;
static PFN_WindowsGetStringRawBuffer p_WindowsGetStringRawBuffer = NULL;
static HRESULT WINAPI My_RoGetActivationFactory(void* activatableClassId, void* iid, void** factory);

static int WINAPI My_GetSystemMetrics(int nIndex)
{
    if (g_hasWindowTarget)
    {
        switch (nIndex)
        {
        case SM_CXSCREEN: case SM_CXFULLSCREEN: return g_winW;
        case SM_CYSCREEN: case SM_CYFULLSCREEN: return g_winH;
        }
    }
    return o_GetSystemMetrics ? o_GetSystemMetrics(nIndex) : 0;
}

static void ClampMonitorInfo(LPMONITORINFO mi)
{
    if (mi == NULL)
    {
        return;
    }
    mi->rcMonitor.left = g_winX;
    mi->rcMonitor.top = g_winY;
    mi->rcMonitor.right = g_winX + g_winW;
    mi->rcMonitor.bottom = g_winY + g_winH;
    mi->rcWork = mi->rcMonitor;
}

static BOOL WINAPI My_GetMonitorInfoW(HMONITOR h, LPMONITORINFO mi)
{
    BOOL r = o_GetMonitorInfoW ? o_GetMonitorInfoW(h, mi) : FALSE;
    if (r && g_hasWindowTarget)
    {
        ClampMonitorInfo(mi);
    }
    return r;
}

static BOOL WINAPI My_GetMonitorInfoA(HMONITOR h, LPMONITORINFO mi)
{
    BOOL r = o_GetMonitorInfoA ? o_GetMonitorInfoA(h, mi) : FALSE;
    if (r && g_hasWindowTarget)
    {
        ClampMonitorInfo(mi);
    }
    return r;
}

static BOOL WINAPI My_EnumDisplaySettingsW(LPCWSTR name, DWORD mode, DEVMODEW* dm)
{
    BOOL r = o_EnumDisplaySettingsW ? o_EnumDisplaySettingsW(name, mode, dm) : FALSE;
    if (r && g_hasWindowTarget && dm != NULL &&
        (mode == ENUM_CURRENT_SETTINGS || mode == ENUM_REGISTRY_SETTINGS))
    {
        dm->dmPelsWidth = (DWORD)g_winW;
        dm->dmPelsHeight = (DWORD)g_winH;
    }
    return r;
}

static BOOL WINAPI My_EnumDisplaySettingsA(LPCSTR name, DWORD mode, DEVMODEA* dm)
{
    BOOL r = o_EnumDisplaySettingsA ? o_EnumDisplaySettingsA(name, mode, dm) : FALSE;
    if (r && g_hasWindowTarget && dm != NULL &&
        (mode == ENUM_CURRENT_SETTINGS || mode == ENUM_REGISTRY_SETTINGS))
    {
        dm->dmPelsWidth = (DWORD)g_winW;
        dm->dmPelsHeight = (DWORD)g_winH;
    }
    return r;
}

static LONG WINAPI My_ChangeDisplaySettingsExW(LPCWSTR dev, DEVMODEW* dm, HWND wnd, DWORD flags, LPVOID lp)
{
    // Never change the real desktop mode; report success so the game proceeds as
    // borderless at our (faked) monitor size instead of a real exclusive mode.
    if (g_hasWindowTarget)
    {
        return DISP_CHANGE_SUCCESSFUL;
    }
    return o_ChangeDisplaySettingsExW ? o_ChangeDisplaySettingsExW(dev, dm, wnd, flags, lp) : DISP_CHANGE_FAILED;
}

// Redirects one named import of a module's IAT to newFunc; returns the original.
static void* PatchIatEntry(HMODULE module, const char* dllName, const char* funcName, void* newFunc)
{
    BYTE* base = (BYTE*)module;
    IMAGE_DOS_HEADER* dos = (IMAGE_DOS_HEADER*)base;
    if (dos->e_magic != IMAGE_DOS_SIGNATURE)
    {
        return NULL;
    }
    IMAGE_NT_HEADERS* nt = (IMAGE_NT_HEADERS*)(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE)
    {
        return NULL;
    }
    IMAGE_DATA_DIRECTORY dir = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (dir.VirtualAddress == 0)
    {
        return NULL;
    }

    IMAGE_IMPORT_DESCRIPTOR* desc = (IMAGE_IMPORT_DESCRIPTOR*)(base + dir.VirtualAddress);
    for (; desc->Name != 0; ++desc)
    {
        const char* name = (const char*)(base + desc->Name);
        if (_stricmp(name, dllName) != 0)
        {
            continue;
        }

        IMAGE_THUNK_DATA* origThunk = (IMAGE_THUNK_DATA*)(base + desc->OriginalFirstThunk);
        IMAGE_THUNK_DATA* iatThunk = (IMAGE_THUNK_DATA*)(base + desc->FirstThunk);
        if (desc->OriginalFirstThunk == 0)
        {
            origThunk = iatThunk;
        }

        for (; origThunk->u1.AddressOfData != 0; ++origThunk, ++iatThunk)
        {
            if (origThunk->u1.Ordinal & IMAGE_ORDINAL_FLAG)
            {
                continue; // imported by ordinal, no name to match
            }
            IMAGE_IMPORT_BY_NAME* ibn = (IMAGE_IMPORT_BY_NAME*)(base + origThunk->u1.AddressOfData);
            if (strcmp((const char*)ibn->Name, funcName) != 0)
            {
                continue;
            }

            void* orig = (void*)(ULONG_PTR)iatThunk->u1.Function;
            DWORD oldProtect = 0;
            if (VirtualProtect(&iatThunk->u1.Function, sizeof(void*), PAGE_READWRITE, &oldProtect))
            {
                iatThunk->u1.Function = (ULONG_PTR)newFunc;
                VirtualProtect(&iatThunk->u1.Function, sizeof(void*), oldProtect, &oldProtect);
                return orig;
            }
            return NULL;
        }
    }
    return NULL;
}

static void HookModuleForResolution(HMODULE mod)
{
    void* o;
    o = PatchIatEntry(mod, "user32.dll", "GetSystemMetrics", (void*)My_GetSystemMetrics);
    if (o != NULL) { o_GetSystemMetrics = (PFN_GetSystemMetrics)o; }
    o = PatchIatEntry(mod, "user32.dll", "GetMonitorInfoW", (void*)My_GetMonitorInfoW);
    if (o != NULL) { o_GetMonitorInfoW = (PFN_GetMonitorInfoW)o; }
    o = PatchIatEntry(mod, "user32.dll", "GetMonitorInfoA", (void*)My_GetMonitorInfoA);
    if (o != NULL) { o_GetMonitorInfoA = (PFN_GetMonitorInfoA)o; }
    o = PatchIatEntry(mod, "user32.dll", "EnumDisplaySettingsW", (void*)My_EnumDisplaySettingsW);
    if (o != NULL) { o_EnumDisplaySettingsW = (PFN_EnumDisplaySettingsW)o; }
    o = PatchIatEntry(mod, "user32.dll", "EnumDisplaySettingsA", (void*)My_EnumDisplaySettingsA);
    if (o != NULL) { o_EnumDisplaySettingsA = (PFN_EnumDisplaySettingsA)o; }
    o = PatchIatEntry(mod, "user32.dll", "ChangeDisplaySettingsExW", (void*)My_ChangeDisplaySettingsExW);
    if (o != NULL) { o_ChangeDisplaySettingsExW = (PFN_ChangeDisplaySettingsExW)o; }
}

static void ApplyResolutionHooks(HMODULE self)
{
    if (!g_hasWindowTarget)
    {
        return;
    }
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, 0);
    if (snap == INVALID_HANDLE_VALUE)
    {
        return;
    }
    MODULEENTRY32W me;
    me.dwSize = sizeof(me);
    if (Module32FirstW(snap, &me))
    {
        do
        {
            if (me.hModule != self) // never patch ourselves
            {
                HookModuleForResolution(me.hModule);
            }
        } while (Module32NextW(snap, &me));
    }
    CloseHandle(snap);
    ProxyLog("resolution: display hooks applied (monitor reported as split region)");
}

// ---- Raw Input isolation -----------------------------------------------
// Unity's input backend reads game controllers through Raw Input (WM_INPUT),
// which completely bypasses XInput - so filtering XInput alone is not enough and
// both pads drove every window. We pick ONE physical gamepad for this instance
// and swallow WM_INPUT from every other gamepad, exactly like Nucleus/Proto Input
// does. Mouse and keyboard raw input always pass through untouched.

static HANDLE g_assignedRawDevice = NULL;   // the gamepad this instance may use
static bool   g_rawFilterReady = false;

// Small cache so the WM_INPUT hot path does not re-query device info each packet.
struct RawDevCacheEntry { HANDLE handle; bool isGamepad; };
static RawDevCacheEntry g_rawCache[64];
static int g_rawCacheCount = 0;

static bool QueryIsGamepad(HANDLE device)
{
    RID_DEVICE_INFO info;
    info.cbSize = sizeof(info);
    UINT size = sizeof(info);
    if (GetRawInputDeviceInfoW(device, RIDI_DEVICEINFO, &info, &size) == (UINT)-1)
    {
        return false;
    }
    if (info.dwType != RIM_TYPEHID)
    {
        return false;
    }
    // Usage page 1 (Generic Desktop), usage 4 (Joystick) or 5 (Gamepad).
    return info.hid.usUsagePage == 0x01 && (info.hid.usUsage == 0x04 || info.hid.usUsage == 0x05);
}

static bool IsGamepadDevice(HANDLE device)
{
    for (int i = 0; i < g_rawCacheCount; ++i)
    {
        if (g_rawCache[i].handle == device)
        {
            return g_rawCache[i].isGamepad;
        }
    }
    bool isPad = QueryIsGamepad(device);
    if (g_rawCacheCount < (int)(sizeof(g_rawCache) / sizeof(g_rawCache[0])))
    {
        g_rawCache[g_rawCacheCount].handle = device;
        g_rawCache[g_rawCacheCount].isGamepad = isPad;
        g_rawCacheCount++;
    }
    return isPad;
}

// Returns true if this WM_INPUT belongs to a gamepad that is NOT ours, so the
// caller should swallow it instead of letting the game read it.
static bool ShouldSwallowRawInput(HRAWINPUT hRawInput)
{
    if (!g_rawFilterReady || g_assignedRawDevice == NULL)
    {
        return false;
    }

    // Use the trampoline (unhooked original) so we never recurse into our own hook.
    PFN_GetRawInputData getData = o_GetRawInputData ? o_GetRawInputData : GetRawInputData;
    RAWINPUTHEADER header;
    UINT size = sizeof(header);
    if (getData(hRawInput, RID_HEADER, &header, &size, sizeof(RAWINPUTHEADER)) == (UINT)-1)
    {
        return false;
    }
    if (header.dwType != RIM_TYPEHID || header.hDevice == NULL)
    {
        return false; // mouse/keyboard: never filter.
    }
    if (header.hDevice == g_assignedRawDevice)
    {
        return false; // our controller.
    }
    return IsGamepadDevice(header.hDevice); // a foreign gamepad -> swallow.
}

// Logs (once) whether the game actually reads gamepads through the Raw Input API,
// so we can tell if filtering here can possibly work or if it uses another API.
static void LogRawGamepadOnce(bool ours, bool viaBuffer)
{
    static volatile LONG loggedRead = 0;
    static volatile LONG loggedSwallow = 0;
    if (InterlockedCompareExchange(&loggedRead, 1, 0) == 0)
    {
        char m[160];
        sprintf_s(m, sizeof(m), "rawinput READ: game reads a gamepad via %s (this one is %s)",
                  viaBuffer ? "GetRawInputBuffer" : "GetRawInputData", ours ? "OURS" : "foreign");
        ProxyLog(m);
    }
    if (!ours && InterlockedCompareExchange(&loggedSwallow, 1, 0) == 0)
    {
        ProxyLog("rawinput READ: dropped a foreign gamepad (filter is doing its job)");
    }
}

// Hooked GetRawInputData. Input from a foreign gamepad is reported as a failed read
// (-1) so the game ignores that device. Mouse, keyboard and our own pad pass through.
static UINT WINAPI My_GetRawInputData(HRAWINPUT hRawInput, UINT uiCommand, LPVOID pData,
                                      PUINT pcbSize, UINT cbSizeHeader)
{
    if (g_rawFilterReady && (uiCommand == RID_INPUT || uiCommand == RID_HEADER) &&
        g_assignedRawDevice != NULL)
    {
        PFN_GetRawInputData getData = o_GetRawInputData ? o_GetRawInputData : GetRawInputData;
        RAWINPUTHEADER header;
        UINT hsize = sizeof(header);
        if (getData(hRawInput, RID_HEADER, &header, &hsize, sizeof(RAWINPUTHEADER)) != (UINT)-1 &&
            header.dwType == RIM_TYPEHID && header.hDevice != NULL &&
            IsGamepadDevice(header.hDevice))
        {
            bool ours = (header.hDevice == g_assignedRawDevice);
            LogRawGamepadOnce(ours, /*viaBuffer*/ false);
            if (!ours)
            {
                if (pcbSize != NULL)
                {
                    *pcbSize = 0;
                }
                return (UINT)-1; // failed read -> the game skips this device.
            }
        }
    }
    return o_GetRawInputData ? o_GetRawInputData(hRawInput, uiCommand, pData, pcbSize, cbSizeHeader)
                             : (UINT)-1;
}

// Hooked GetRawInputBuffer (buffered/polled path): physically REMOVE foreign-gamepad
// entries by compacting the buffer and reducing the returned count.
static UINT WINAPI My_GetRawInputBuffer(PRAWINPUT pData, PUINT pcbSize, UINT cbSizeHeader)
{
    UINT r = o_GetRawInputBuffer ? o_GetRawInputBuffer(pData, pcbSize, cbSizeHeader) : (UINT)-1;
    if (!g_rawFilterReady || pData == NULL || r == (UINT)-1 || r == 0)
    {
        return r;
    }

    RAWINPUT* read = pData;
    RAWINPUT* write = pData;
    UINT kept = 0;
    for (UINT i = 0; i < r; ++i)
    {
        RAWINPUT* next = NEXTRAWINPUTBLOCK(read);
        UINT blockSize = (UINT)((BYTE*)next - (BYTE*)read);

        bool isPad = (read->header.dwType == RIM_TYPEHID && read->header.hDevice != NULL &&
                      IsGamepadDevice(read->header.hDevice));
        bool foreign = isPad && read->header.hDevice != g_assignedRawDevice;
        if (isPad)
        {
            LogRawGamepadOnce(!foreign, /*viaBuffer*/ true);
        }

        if (!foreign)
        {
            if (write != read)
            {
                memmove(write, read, blockSize);
            }
            write = (RAWINPUT*)((BYTE*)write + blockSize);
            kept++;
        }
        read = next;
    }
    return kept;
}

// Drops a foreign-gamepad WM_INPUT message by turning it into a harmless WM_NULL,
// exactly like Nucleus/ProtoInput. Returns true if it blanked the message.
static bool BlankIfForeignRawInput(LPMSG msg)
{
    if (msg == NULL || msg->message != WM_INPUT || !g_rawFilterReady || g_assignedRawDevice == NULL)
    {
        return false;
    }
    if (ShouldSwallowRawInput((HRAWINPUT)msg->lParam))
    {
        static volatile LONG logged = 0;
        if (InterlockedCompareExchange(&logged, 1, 0) == 0)
        {
            ProxyLog("rawinput MSG: blanked a foreign gamepad WM_INPUT (message-pump filter active)");
        }
        memset(msg, 0, sizeof(MSG)); // WM_NULL: the game's loop ignores it.
        return true;
    }
    return false;
}

static BOOL WINAPI My_GetMessageW(LPMSG m, HWND h, UINT mn, UINT mx)
{
    BOOL r = o_GetMessageW(m, h, mn, mx);
    if (r && r != -1) { BlankIfForeignRawInput(m); }
    return r;
}

static BOOL WINAPI My_GetMessageA(LPMSG m, HWND h, UINT mn, UINT mx)
{
    BOOL r = o_GetMessageA(m, h, mn, mx);
    if (r && r != -1) { BlankIfForeignRawInput(m); }
    return r;
}

static BOOL WINAPI My_PeekMessageW(LPMSG m, HWND h, UINT mn, UINT mx, UINT rm)
{
    BOOL r = o_PeekMessageW(m, h, mn, mx, rm);
    // Only blank when the message is being removed; blanking a PM_NOREMOVE peek
    // would leave the real WM_INPUT in the queue and could spin the game.
    if (r && (rm & PM_REMOVE)) { BlankIfForeignRawInput(m); }
    return r;
}

static BOOL WINAPI My_PeekMessageA(LPMSG m, HWND h, UINT mn, UINT mx, UINT rm)
{
    BOOL r = o_PeekMessageA(m, h, mn, mx, rm);
    if (r && (rm & PM_REMOVE)) { BlankIfForeignRawInput(m); }
    return r;
}

// Builds a lowercase match key from a HID device path (the instance-specific part
// before the interface GUID), e.g. "hid#vid_2dc8&pid_310a&ig_01#b&3eaa583&0&0000".
static void AddForeignKey(const char* devPath)
{
    const char* hid = strstr(devPath, "HID#");
    if (hid == NULL) { hid = strstr(devPath, "hid#"); }
    if (hid == NULL) { return; }
    char key[200];
    int j = 0;
    for (int i = 0; hid[i] != 0 && j < 198; ++i)
    {
        if (hid[i] == '#' && hid[i + 1] == '{') { break; } // stop before the GUID
        char c = hid[i];
        if (c >= 'A' && c <= 'Z') { c = (char)(c - 'A' + 'a'); }
        key[j++] = c;
    }
    key[j] = 0;
    if (j > 10 && g_foreignKeyCount < 8)
    {
        strcpy_s(g_foreignKeys[g_foreignKeyCount++], sizeof(g_foreignKeys[0]), key);
    }
}

static bool IsForeignHidPath(const char* lower)
{
    if (g_foreignKeyCount == 0 || strstr(lower, "hid#") == NULL) { return false; }
    for (int i = 0; i < g_foreignKeyCount; ++i)
    {
        if (strstr(lower, g_foreignKeys[i]) != NULL) { return true; }
    }
    return false;
}

static bool ToLowerDevicePath(const wchar_t* name, char* out, int outSize)
{
    if (name == NULL || name[0] != L'\\' || name[1] != L'\\') { return false; }
    int j = 0;
    for (int i = 0; name[i] != 0 && j < outSize - 1; ++i)
    {
        wchar_t w = name[i];
        char ch = (w < 128) ? (char)w : '?';
        if (ch >= 'A' && ch <= 'Z') { ch = (char)(ch - 'A' + 'a'); }
        out[j++] = ch;
    }
    out[j] = 0;
    return true;
}

static void LogHidBlockOnce()
{
    static volatile LONG logged = 0;
    if (InterlockedCompareExchange(&logged, 1, 0) == 0)
    {
        ProxyLog("hid: blocked direct open of a foreign gamepad device");
    }
}

static HANDLE WINAPI My_CreateFileW(LPCWSTR name, DWORD access, DWORD share, LPSECURITY_ATTRIBUTES sec,
                                    DWORD disp, DWORD flags, HANDLE templ)
{
    if (g_rawFilterReady)
    {
        char buf[300];
        if (ToLowerDevicePath(name, buf, sizeof(buf)) && IsForeignHidPath(buf))
        {
            LogHidBlockOnce();
            SetLastError(ERROR_ACCESS_DENIED);
            return INVALID_HANDLE_VALUE;
        }
    }
    return o_CreateFileW(name, access, share, sec, disp, flags, templ);
}

static HANDLE WINAPI My_CreateFileA(LPCSTR name, DWORD access, DWORD share, LPSECURITY_ATTRIBUTES sec,
                                    DWORD disp, DWORD flags, HANDLE templ)
{
    if (g_rawFilterReady && name != NULL && name[0] == '\\' && name[1] == '\\')
    {
        char buf[300];
        int j = 0;
        for (int i = 0; name[i] != 0 && j < (int)sizeof(buf) - 1; ++i)
        {
            char ch = name[i];
            if (ch >= 'A' && ch <= 'Z') { ch = (char)(ch - 'A' + 'a'); }
            buf[j++] = ch;
        }
        buf[j] = 0;
        if (IsForeignHidPath(buf))
        {
            LogHidBlockOnce();
            SetLastError(ERROR_ACCESS_DENIED);
            return INVALID_HANDLE_VALUE;
        }
    }
    return o_CreateFileA(name, access, share, sec, disp, flags, templ);
}

static void LogWgiBlockOnce()
{
    static volatile LONG logged = 0;
    if (InterlockedCompareExchange(&logged, 1, 0) == 0)
    {
        ProxyLog("wgi: blocked RoGetActivationFactory for Windows.Gaming.Input");
    }
}

static HRESULT WINAPI My_RoGetActivationFactory(void* activatableClassId, void* iid, void** factory)
{
    if (g_rawFilterReady && activatableClassId != NULL && p_WindowsGetStringRawBuffer != NULL)
    {
        UINT32 len = 0;
        PCWSTR str = p_WindowsGetStringRawBuffer(activatableClassId, &len);
        if (str != NULL && len >= 20)
        {
            // Check if it starts with "Windows.Gaming.Input"
            if (wcsncmp(str, L"Windows.Gaming.Input", 20) == 0)
            {
                LogWgiBlockOnce();
                return 0x80040154; // REGDB_E_CLASSNOTREG
            }
        }
    }
    return o_RoGetActivationFactory(activatableClassId, iid, factory);
}

// Installs INLINE hooks (via MinHook) on the Raw Input read functions inside
// user32 itself. Unlike IAT patching, this catches every caller regardless of how
// it resolved the function - which is required for Unity, whose engine does not go
// through its import table for these. This is the same mechanism Nucleus/ProtoInput
// use to make a game respond to only one controller.
static void InstallRawInputHooks()
{
    MH_STATUS init = MH_Initialize();
    if (init != MH_OK && init != MH_ERROR_ALREADY_INITIALIZED)
    {
        ProxyLog("rawinput: MinHook init failed");
        return;
    }

    HMODULE user32 = GetModuleHandleA("user32.dll");
    if (user32 == NULL)
    {
        return;
    }

    void* pGetData = (void*)GetProcAddress(user32, "GetRawInputData");
    void* pGetBuffer = (void*)GetProcAddress(user32, "GetRawInputBuffer");

    int installed = 0;
    if (pGetData != NULL &&
        MH_CreateHook(pGetData, (void*)My_GetRawInputData, (void**)&o_GetRawInputData) == MH_OK &&
        MH_EnableHook(pGetData) == MH_OK)
    {
        installed++;
    }
    if (pGetBuffer != NULL &&
        MH_CreateHook(pGetBuffer, (void*)My_GetRawInputBuffer, (void**)&o_GetRawInputBuffer) == MH_OK &&
        MH_EnableHook(pGetBuffer) == MH_OK)
    {
        installed++;
    }

    // Message-pump hooks: blank foreign-gamepad WM_INPUT before the game reads it.
    int msgHooks = 0;
    struct { const char* name; void* detour; void** orig; } mh[] = {
        { "GetMessageW",  (void*)My_GetMessageW,  (void**)&o_GetMessageW },
        { "GetMessageA",  (void*)My_GetMessageA,  (void**)&o_GetMessageA },
        { "PeekMessageW", (void*)My_PeekMessageW, (void**)&o_PeekMessageW },
        { "PeekMessageA", (void*)My_PeekMessageA, (void**)&o_PeekMessageA },
    };
    for (int i = 0; i < 4; ++i)
    {
        void* p = (void*)GetProcAddress(user32, mh[i].name);
        if (p != NULL && MH_CreateHook(p, mh[i].detour, mh[i].orig) == MH_OK && MH_EnableHook(p) == MH_OK)
        {
            msgHooks++;
        }
    }

    // Block direct HID opens of foreign gamepads (kernel32!CreateFile).
    int hidHooks = 0;
    HMODULE k32 = GetModuleHandleA("kernel32.dll");
    if (k32 != NULL)
    {
        void* pcw = (void*)GetProcAddress(k32, "CreateFileW");
        void* pca = (void*)GetProcAddress(k32, "CreateFileA");
        if (pcw != NULL && MH_CreateHook(pcw, (void*)My_CreateFileW, (void**)&o_CreateFileW) == MH_OK &&
            MH_EnableHook(pcw) == MH_OK) { hidHooks++; }
        if (pca != NULL && MH_CreateHook(pca, (void*)My_CreateFileA, (void**)&o_CreateFileA) == MH_OK &&
            MH_EnableHook(pca) == MH_OK) { hidHooks++; }
    }

    // Block Windows Gaming Input (combase!RoGetActivationFactory).
    int wgiHooks = 0;
    HMODULE combase = GetModuleHandleA("combase.dll");
    if (combase == NULL)
    {
        combase = LoadLibraryA("combase.dll");
    }
    if (combase != NULL)
    {
        p_WindowsGetStringRawBuffer = (PFN_WindowsGetStringRawBuffer)GetProcAddress(combase, "WindowsGetStringRawBuffer");
        void* pRoGet = (void*)GetProcAddress(combase, "RoGetActivationFactory");
        if (pRoGet != NULL && p_WindowsGetStringRawBuffer != NULL &&
            MH_CreateHook(pRoGet, (void*)My_RoGetActivationFactory, (void**)&o_RoGetActivationFactory) == MH_OK &&
            MH_EnableHook(pRoGet) == MH_OK)
        {
            wgiHooks++;
        }
    }

    char msg[160];
    sprintf_s(msg, sizeof(msg), "rawinput: inline hooks installed (data %d/2, msgpump %d/4, hid %d/2, wgi %d/1)",
              installed, msgHooks, hidHooks, wgiHooks);
    ProxyLog(msg);
}

// Enumerates all raw gamepads, sorts them by device path for a stable order, and
// claims the one at our assigned index for this instance.
static void AssignRawDevice()
{
    UINT count = 0;
    if (GetRawInputDeviceList(NULL, &count, sizeof(RAWINPUTDEVICELIST)) == (UINT)-1 || count == 0)
    {
        ProxyLog("rawinput: no devices");
        return;
    }

    RAWINPUTDEVICELIST* list = (RAWINPUTDEVICELIST*)malloc(sizeof(RAWINPUTDEVICELIST) * count);
    if (list == NULL)
    {
        return;
    }
    UINT got = GetRawInputDeviceList(list, &count, sizeof(RAWINPUTDEVICELIST));
    if (got == (UINT)-1)
    {
        free(list);
        return;
    }

    // Collect gamepad handles + their device paths.
    HANDLE pads[16];
    char   paths[16][260];
    int    padCount = 0;

    for (UINT i = 0; i < got && padCount < 16; ++i)
    {
        if (list[i].dwType != RIM_TYPEHID || !IsGamepadDevice(list[i].hDevice))
        {
            continue;
        }
        char name[260] = { 0 };
        UINT nameLen = sizeof(name);
        GetRawInputDeviceInfoA(list[i].hDevice, RIDI_DEVICENAME, name, &nameLen);
        pads[padCount] = list[i].hDevice;
        strncpy_s(paths[padCount], sizeof(paths[padCount]), name, _TRUNCATE);
        padCount++;
    }
    free(list);

    // Stable sort by device path (simple insertion sort; tiny n).
    for (int i = 1; i < padCount; ++i)
    {
        HANDLE h = pads[i];
        char p[260];
        strncpy_s(p, sizeof(p), paths[i], _TRUNCATE);
        int j = i - 1;
        while (j >= 0 && strcmp(paths[j], p) > 0)
        {
            pads[j + 1] = pads[j];
            strncpy_s(paths[j + 1], sizeof(paths[j + 1]), paths[j], _TRUNCATE);
            j--;
        }
        pads[j + 1] = h;
        strncpy_s(paths[j + 1], sizeof(paths[j + 1]), p, _TRUNCATE);
    }

    char msg[400];
    sprintf_s(msg, sizeof(msg), "rawinput: found %d gamepad(s), claiming index %lu", padCount, g_assignedIndex);
    ProxyLog(msg);
    for (int i = 0; i < padCount; ++i)
    {
        sprintf_s(msg, sizeof(msg), "rawinput: [%d] %s", i, paths[i]);
        ProxyLog(msg);
    }

    if ((int)g_assignedIndex < padCount)
    {
        g_assignedRawDevice = pads[g_assignedIndex];
        // Remember every OTHER gamepad so we can block direct HID opens of them.
        for (int i = 0; i < padCount; ++i)
        {
            if (i != (int)g_assignedIndex) { AddForeignKey(paths[i]); }
        }
        g_rawFilterReady = true;
        ProxyLog("rawinput: filter active (only the claimed gamepad reaches this instance)");
    }
    else
    {
        ProxyLog("rawinput: assigned index out of range, filter inactive");
    }
}

// ---- Window enforcement -------------------------------------------------
// Pins the game window to the split region the launcher assigned by clamping
// every WM_WINDOWPOSCHANGING. Runs inside the game process, so the window
// physically cannot resize itself to fullscreen during an intro.

static LRESULT CALLBACK SplitWndProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_WINDOWPOSCHANGING:
        if (g_hasWindowTarget)
        {
            WINDOWPOS* wp = (WINDOWPOS*)lParam;
            wp->x = g_winX;
            wp->y = g_winY;
            wp->cx = g_winW;
            wp->cy = g_winH;
            wp->flags &= ~((UINT)SWP_NOSIZE | (UINT)SWP_NOMOVE);
            wp->flags |= (UINT)SWP_NOZORDER;
        }
        break;

    // Fake focus. Each split instance must keep running and reading its own
    // controller even when its window sits in the background, so both players
    // play simultaneously. Many engines (Unity with "Run In Background" off, in
    // particular) pause the game loop and ignore input when they think they lost
    // focus, which is why only the foreground window responded. We tell the game
    // it is always the active/foreground app. Real OS focus is unaffected, so the
    // keyboard/mouse still go wherever the user clicks.
    case WM_ACTIVATEAPP:
        wParam = TRUE;
        break;
    case WM_ACTIVATE:
        wParam = WA_ACTIVE;
        lParam = 0;
        break;
    case WM_NCACTIVATE:
        wParam = TRUE;
        break;
    case WM_KILLFOCUS:
        return 0; // Swallow focus loss.

    case WM_INPUT:
        // Drop raw input coming from any gamepad other than this instance's.
        // We must still let DefWindowProc clean the raw input up.
        if (ShouldSwallowRawInput((HRAWINPUT)lParam))
        {
            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }
        break;
    }

    return CallWindowProcW(g_origProc, hWnd, msg, wParam, lParam);
}

struct FindWindowCtx { DWORD pid; HWND result; };

static BOOL CALLBACK FindMainWindowProc(HWND hWnd, LPARAM lParam)
{
    FindWindowCtx* ctx = (FindWindowCtx*)lParam;
    DWORD pid = 0;
    GetWindowThreadProcessId(hWnd, &pid);
    if (pid != ctx->pid || !IsWindowVisible(hWnd) || GetWindow(hWnd, GW_OWNER) != NULL)
    {
        return TRUE;
    }
    RECT r;
    if (GetWindowRect(hWnd, &r) && (r.right - r.left) > 100 && (r.bottom - r.top) > 100)
    {
        ctx->result = hWnd;
        return FALSE;
    }
    return TRUE;
}

static void StripWindowBorders(HWND hWnd)
{
    LONG_PTR style = GetWindowLongPtrW(hWnd, GWL_STYLE);
    style &= ~((LONG_PTR)(WS_CAPTION | WS_THICKFRAME | WS_BORDER | WS_DLGFRAME));
    SetWindowLongPtrW(hWnd, GWL_STYLE, style);

    LONG_PTR ex = GetWindowLongPtrW(hWnd, GWL_EXSTYLE);
    ex &= ~((LONG_PTR)(WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE));
    SetWindowLongPtrW(hWnd, GWL_EXSTYLE, ex);
}

static DWORD WINAPI WindowEnforceThread(LPVOID)
{
    if (!g_hasWindowTarget)
    {
        return 0;
    }

    HWND wnd = NULL;
    for (int i = 0; i < 600 && wnd == NULL; ++i)   // up to ~60s for the window to appear
    {
        FindWindowCtx ctx = { GetCurrentProcessId(), NULL };
        EnumWindows(FindMainWindowProc, (LPARAM)&ctx);
        wnd = ctx.result;
        if (wnd == NULL)
        {
            Sleep(100);
        }
    }
    if (wnd == NULL)
    {
        ProxyLog("window enforce: no main window found");
        return 0;
    }

    // Confine the game's idea of the monitor to our split region before it gets a
    // chance to switch to fullscreen. The game modules are loaded by now.
    HMODULE self = NULL;
    GetModuleHandleExW(
        GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        (LPCWSTR)(void*)&ApplyResolutionHooks, &self);
    ApplyResolutionHooks(self);

    g_hookedWnd = wnd;
    g_origProc = (WNDPROC)SetWindowLongPtrW(wnd, GWLP_WNDPROC, (LONG_PTR)SplitWndProc);

    StripWindowBorders(wnd);
    SetWindowPos(wnd, NULL, g_winX, g_winY, g_winW, g_winH,
                 SWP_NOZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
    ProxyLog("window enforce: hooked main window + pinned to region");
    return 0;
}

// Only index 0 maps to the assigned physical pad; everything else is "empty".
static inline bool MapIndex(DWORD requested, DWORD& real)
{
    if (requested != 0)
    {
        return false;
    }
    real = g_assignedIndex;
    return true;
}

extern "C" {

// Logs the first time the game reads any XInput state, so we can tell whether the
// game uses XInput at all (if this never appears, it reads pads via another API).
static void LogFirstXInputCall(DWORD requestedIndex)
{
    static volatile LONG logged = 0;
    if (InterlockedCompareExchange(&logged, 1, 0) == 0)
    {
        char msg[160];
        sprintf_s(msg, sizeof(msg),
                  "game called XInputGetState (requested index %lu) -> using XInput", requestedIndex);
        ProxyLog(msg);
    }
}

DWORD WINAPI XInputGetState(DWORD dwUserIndex, XINPUT_STATE* pState)
{
    EnsureInit();
    LogFirstXInputCall(dwUserIndex);
    RefreshAssignedIndex();
    DWORD real;
    if (!MapIndex(dwUserIndex, real) || p_GetState == NULL)
    {
        return ERROR_DEVICE_NOT_CONNECTED;
    }
    return p_GetState(real, pState);
}

// Hidden ordinal-100 variant. Falls back to the normal call if unavailable.
DWORD WINAPI XInputGetStateEx(DWORD dwUserIndex, XINPUT_STATE* pState)
{
    EnsureInit();
    LogFirstXInputCall(dwUserIndex);
    RefreshAssignedIndex();
    DWORD real;
    if (!MapIndex(dwUserIndex, real))
    {
        return ERROR_DEVICE_NOT_CONNECTED;
    }
    if (p_GetStateEx != NULL)
    {
        return p_GetStateEx(real, pState);
    }
    if (p_GetState != NULL)
    {
        return p_GetState(real, pState);
    }
    return ERROR_DEVICE_NOT_CONNECTED;
}

DWORD WINAPI XInputSetState(DWORD dwUserIndex, XINPUT_VIBRATION* pVibration)
{
    EnsureInit();
    DWORD real;
    if (!MapIndex(dwUserIndex, real) || p_SetState == NULL)
    {
        return ERROR_DEVICE_NOT_CONNECTED;
    }
    return p_SetState(real, pVibration);
}

DWORD WINAPI XInputGetCapabilities(DWORD dwUserIndex, DWORD dwFlags, XINPUT_CAPABILITIES* pCapabilities)
{
    EnsureInit();
    DWORD real;
    if (!MapIndex(dwUserIndex, real) || p_GetCapabilities == NULL)
    {
        return ERROR_DEVICE_NOT_CONNECTED;
    }
    return p_GetCapabilities(real, dwFlags, pCapabilities);
}

void WINAPI XInputEnable(BOOL enable)
{
    EnsureInit();
    if (p_Enable != NULL)
    {
        p_Enable(enable);
    }
}

DWORD WINAPI XInputGetBatteryInformation(DWORD dwUserIndex, BYTE devType, XINPUT_BATTERY_INFORMATION* pBattery)
{
    EnsureInit();
    DWORD real;
    if (!MapIndex(dwUserIndex, real) || p_GetBattery == NULL)
    {
        return ERROR_DEVICE_NOT_CONNECTED;
    }
    return p_GetBattery(real, devType, pBattery);
}

DWORD WINAPI XInputGetKeystroke(DWORD dwUserIndex, DWORD dwReserved, PXINPUT_KEYSTROKE pKeystroke)
{
    EnsureInit();
    DWORD real;
    if (!MapIndex(dwUserIndex, real) || p_GetKeystroke == NULL)
    {
        return ERROR_DEVICE_NOT_CONNECTED;
    }
    return p_GetKeystroke(real, dwReserved, pKeystroke);
}

DWORD WINAPI XInputGetDSoundAudioDeviceGuids(DWORD dwUserIndex, GUID* pDSoundRenderGuid, GUID* pDSoundCaptureGuid)
{
    EnsureInit();
    DWORD real;
    if (!MapIndex(dwUserIndex, real) || p_GetDSoundGuids == NULL)
    {
        return ERROR_DEVICE_NOT_CONNECTED;
    }
    return p_GetDSoundGuids(real, pDSoundRenderGuid, pDSoundCaptureGuid);
}

} // extern "C"

static DWORD WINAPI InitThread(LPVOID)
{
    EnsureInit();
    return 0;
}

BOOL APIENTRY DllMain(HMODULE, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        // Spawn a thread to initialize hooks immediately without blocking the
        // loader lock. This ensures input is hooked before the game reads it
        // even if it never calls XInputGetState.
        HANDLE th = CreateThread(NULL, 0, InitThread, NULL, 0, NULL);
        if (th != NULL)
        {
            CloseHandle(th);
        }
    }
    return TRUE;
}
