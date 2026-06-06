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
#include <stdlib.h>

// XInputGetStateEx (hidden ordinal 100) exposes the Guide button. Many games
// import it by ordinal; we mirror it.
typedef DWORD(WINAPI* PFN_GetState)(DWORD, XINPUT_STATE*);
typedef DWORD(WINAPI* PFN_SetState)(DWORD, XINPUT_VIBRATION*);
typedef DWORD(WINAPI* PFN_GetCapabilities)(DWORD, DWORD, XINPUT_CAPABILITIES*);
typedef void (WINAPI* PFN_Enable)(BOOL);
typedef DWORD(WINAPI* PFN_GetBattery)(DWORD, BYTE, XINPUT_BATTERY_INFORMATION*);
typedef DWORD(WINAPI* PFN_GetKeystroke)(DWORD, DWORD, PXINPUT_KEYSTROKE);
typedef DWORD(WINAPI* PFN_GetDSoundGuids)(DWORD, GUID*, GUID*);

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

    // Load the genuine implementation by full system path (never by bare name, or
    // we would load ourselves from the application directory).
    char systemDir[MAX_PATH] = { 0 };
    UINT n = GetSystemDirectoryA(systemDir, MAX_PATH);
    if (n > 0 && n < MAX_PATH)
    {
        char path[MAX_PATH] = { 0 };
        wsprintfA(path, "%s\\xinput1_4.dll", systemDir);
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

    InterlockedExchange(&g_initState, 2);
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

DWORD WINAPI XInputGetState(DWORD dwUserIndex, XINPUT_STATE* pState)
{
    EnsureInit();
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

BOOL APIENTRY DllMain(HMODULE, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        // Nothing here on purpose - initialisation happens lazily in EnsureInit()
        // to avoid calling LoadLibrary under the loader lock.
    }
    return TRUE;
}
