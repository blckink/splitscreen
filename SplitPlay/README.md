# SplitPlay

A clean, modern Windows app for playing online co-op Steam games in **vertical or
horizontal split-screen on a single PC with two controllers**.

SplitPlay is a from-scratch reimagining of the Nucleus Co-op idea (the original
fork lives under `../Master` as reference). The goals are a friendlier UI, a
modular and individually-updatable codebase, and **per-game configuration through
the interface** instead of downloading and managing handler script files.

> Status: **Testable MVP with per-window controller isolation.** Scanning, the full
> UI, controller detection + rumble test, and per-game profiles work. **Start**
> launches the game executable twice, finds each window, makes it borderless and
> tiles it into the chosen split. Each instance runs behind a **per-instance XInput
> proxy** so it only sees its assigned controller (one pad → one window, even in
> the background); **keyboard and mouse stay free** for the desktop. Single-instance
> games fall back per-slot to a SplitPlay **test window**; a **Test mode** opens
> placeholder windows only, to verify layout safely.
>
> The XInput proxy is a small native DLL that must be built once - see
> [Building the XInput proxy](#building-the-xinput-proxy). Without it the app still
> runs; it just reports isolation as off.

## Scope (MVP)

- Exactly **2 players**, each using a **controller** (XInput / Xbox-style).
- Split the screen **vertically (left/right)** or **horizontally (top/bottom)**.
- Keyboard + mouse players are intentionally **not** supported.

## Tech stack

- **.NET 8** / **WPF** (MVVM), C# with nullable reference types enabled.
- `Microsoft.Extensions.DependencyInjection` for composition.
- No external UI frameworks; the dark theme is a single, retunable resource file.

## Solution layout

The solution is split into focused modules so each concern can evolve and be
updated on its own. Dependencies only ever point *toward* `Core`.

| Project | Target | Responsibility |
|---|---|---|
| `SplitPlay.Core` | `net8.0` | Domain models, abstractions (interfaces), pure layout math. No UI/OS deps — unit-testable. |
| `SplitPlay.Steam` | `net8.0-windows` | Locate Steam, parse libraries (`libraryfolders.vdf`) and manifests (`appmanifest_*.acf`), resolve artwork (local cache → Steam CDN fallback). |
| `SplitPlay.Input` | `net8.0-windows` | XInput controller discovery + connect/disconnect monitoring. |
| `SplitPlay.Launch` | `net8.0-windows` | Process launch, window locating + borderless placement, the real launch engine. |
| `SplitPlay.TestTarget` | `net8.0-windows` | Tiny WinForms test window (placeholder + live controller readout) bundled with the app. |
| `SplitPlay.App` | `net8.0-windows` | WPF presentation: views, view models, DI composition root. |

```
SplitPlay/
├─ SplitPlay.sln
├─ Directory.Build.props        # shared compiler settings
├─ native/                      # native XInput proxy (C++), built via build-proxy.cmd
│  └─ SplitPlay.XInputProxy/    # proxy.cpp, exports.def, .vcxproj
└─ src/
   ├─ SplitPlay.Core/           # Models/, Abstractions/, Services/
   ├─ SplitPlay.Steam/          # Vdf/, scanner, artwork
   ├─ SplitPlay.Input/          # Native/XInput, XInputReader, gamepad service
   ├─ SplitPlay.Launch/         # Native/User32, WindowManager, ExecutableResolver,
   │                            #   GameWindowLocator, RealLaunchEngine
   ├─ SplitPlay.TestTarget/     # WinForms placeholder/test window
   └─ SplitPlay.App/            # Mvvm/, Services/, ViewModels/, Views/, Themes/
```

## Architecture notes

- **MVVM, view-first templating.** `App.xaml` maps each page view model to its
  view via `DataTemplate`s, so navigation is just swapping the bound view model.
- **Decoupling via interfaces.** View models depend on `Core` abstractions
  (`ISteamLibraryScanner`, `IGamepadService`, `ILaunchEngine`, …). Swapping an
  implementation is a one-line change in `AppBootstrapper`.
- **Per-game profiles** are stored as small JSON files in
  `%AppData%/SplitPlay/profiles/{appid}.json`, written atomically.
- **Responsive grid.** The games grid uses a `WrapPanel` with horizontal
  scrolling disabled, so tiles reflow by window width with uniform spacing and
  never produce a horizontal scrollbar.
- **Pixel-accurate layout.** The app is Per-Monitor-v2 DPI aware; the pure
  `SplitLayoutCalculator` tiles a monitor's bounds exactly (no rounding gaps).
- **Per-window controller isolation.** A native XInput proxy DLL
  (`native/SplitPlay.XInputProxy`) is dropped into the game folder (originals are
  backed up and restored). Each instance is launched with the
  `SPLITPLAY_XINPUT_INDEX` env var, so the proxy exposes only that one physical pad
  as index 0 and reports the rest as disconnected. This works at the API level, so
  it holds even when a window is in the background, and it never touches keyboard or
  mouse. `InputIsolationManager` handles install/backup and crash-safe restore.

## How a session is configured

1. **Games** page scans Steam and shows installed games as cover tiles.
2. Click a game → detail page: choose **split orientation**, **display**, and a
   **controller per player** (validated to be distinct), with a live preview.
3. **Start** builds a `LaunchRequest` (regions + controller routing) and hands it
   to the `ILaunchEngine`.

## Roadmap (next)

- Isolation fallback via runtime injection for the rare games that load XInput in
  a way the folder proxy can't shadow (e.g. a hardcoded System32 path).
- Instance lifecycle: track launched processes, clean teardown, relaunch.
- Smarter executable/second-instance handling (read Steam launch config; handle
  launcher→game hand-off; single-instance mutex strategies).
- Instance strategies (`InstanceStrategy`): mirrored copy + emulator
  (Goldberg/Nemirtingas) and dual real Steam accounts.
- Auto-detection of per-game settings (replacing handler files entirely).
- More than two players, richer controller info (battery/live input), themes.

## Building

Requires the **.NET 8 SDK** and **Windows** (WPF + Win32). From the `SplitPlay`
folder:

```powershell
dotnet build SplitPlay.sln
dotnet run --project src/SplitPlay.App/SplitPlay.App.csproj
```

### Building the XInput proxy

Controller isolation needs the small native proxy DLL. This requires the
**"Desktop development with C++"** workload (or the standalone C++ Build Tools).
Build it once (re-run only if `proxy.cpp` changes):

```cmd
native\build-proxy.cmd
```

This produces `native\bin\x64\SplitPlay.XInputProxy.dll` and the x86 variant. The
app build copies them into its output automatically. If the proxy is missing the
app still runs - it just reports controller isolation as off.
