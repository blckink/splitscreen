# SplitPlay installer

This folder builds a single, shareable installer: **`output\SplitPlaySetup.exe`**.

## Build it (one command, no manual setup)

From the repository, double-click **`build-release.cmd`** (or run it in a
terminal). It will, automatically:

1. **Install any missing build tools** via `winget` (asks for admin once):
   - .NET 8 SDK
   - Visual C++ Build Tools (for the native XInput proxy)
   - Inno Setup 6 (to build the installer)
2. Build the native **XInput proxy** (x64 + x86).
3. **Publish SplitPlay self-contained** (the .NET runtime, the test window and the
   proxy are all bundled in).
4. Compile the installer to `installer\output\SplitPlaySetup.exe`.

```cmd
installer\build-release.cmd
```

> First run needs an internet connection (to fetch the tools). If a tool was just
> installed and a step can't find it, open a **new terminal** and run again - the
> PATH only updates in new shells.

Options (when run from a terminal):

```powershell
# Skip the tool auto-install (assume everything is already installed)
powershell -ExecutionPolicy Bypass -File installer\build-release.ps1 -SkipBootstrap
```

## What the end user gets

`SplitPlaySetup.exe` is fully self-contained:

- **No .NET install required** - the runtime is embedded.
- **No Visual C++ runtime required** - the proxy links the CRT statically.
- **No manual proxy build** - the prebuilt proxy ships inside.

The user just runs setup, picks a folder, and launches SplitPlay. Uninstall is
available from Windows "Apps & features".

## Notes

- The installer needs administrator rights (it installs into Program Files).
- SplitPlay itself runs **without** elevation. Controller isolation writes a proxy
  DLL into a game's folder; if a particular game lives under a write-protected
  location, isolation for that game is skipped and reported - move the Steam
  library or run SplitPlay as admin if needed.
