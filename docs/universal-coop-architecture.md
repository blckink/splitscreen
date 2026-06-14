# SplitRoast — Universal Co-op Engine

> Goal: turn a single-player-looking Steam/Epic/GOG game into local split-screen
> co-op **with zero per-game configuration for the common case**, by *detecting*
> what a game needs instead of shipping a hand-written handler for it.

This document is the architecture we are building toward. It is grounded in how the
best projects in this space work — **Nucleus Co-op / SplitScreen.Me**, **ProtoInput**
(Ilyaki), **Goldberg / gbe_fork** (Detanup01), and the **Nemirtingas** EOS/Galaxy
emulators — and states honestly where a no-config approach can and cannot reach.

## 0. The honest reality (so we don't oversell)

Even Nucleus — the gold standard — runs a *generic handler* that "handles pretty
much all situations", **and still** ships thousands of community `.js` handlers for
the exceptions. A 100%-no-config solution for *every* game does not exist, because
games genuinely differ in three ways that can't always be inferred:

1. **Single-instance locks** (mutexes/lock files) with non-obvious names.
2. **Input API** the game actually reads (XInput / DirectInput / RawInput / WGI).
3. **Online identity/auth** — some games authenticate against *their own* backend
   using a real platform ticket (e.g. No Rest for the Wicked → MoonBackend with a
   Steam web ticket). An emulator can fake the SDK, but not a server-side
   entitlement check it doesn't control.

So our target is precise: **auto-handle the large majority** (Unity/Unreal Steam
games with standard input and SDK-level co-op) with **no config**, and degrade
*gracefully and explainably* for the rest — never "edit this file, try that flag".
Knowledge lives in the **engine**, not in per-game files.

## 1. Pipeline overview

```
Steam/Epic/GOG library
        │  scan + filter tools (done)
        ▼
  ┌──────────────┐   inspect install dir + PE
  │  Detection   │──────────────────────────────►  GameCapabilities
  └──────────────┘   engine · SDKs · DRM · arch · single-instance · input
        │
        ▼
  ┌──────────────┐   derive, no handler file
  │   Recipe     │   exe · window args · emulator choice · isolation set
  └──────────────┘
        │
        ▼
  ┌──────────────┐   per player
  │  Instance    │   mirror folder (hardlinks) · per-id emulator config
  └──────────────┘
        │
        ▼
  ┌──────────────┐   injected into each instance
  │  Isolation   │   ONE controller, ALL input APIs · fake focus · region pin
  └──────────────┘
        │
        ▼
  ┌──────────────┐
  │   Window     │   borderless · tiled to split region
  └──────────────┘
```

## 2. Detection — `GameCapabilities` (the brain)

One read-only pass over the install folder + executable produces everything the
launch needs. No per-game data.

| Axis | Signal (auto) | Drives | Status |
|------|---------------|--------|--------|
| **Engine** | `UnityPlayer.dll`, `*_Data/`, `Engine/Binaries/Win64` | window args, registry tweaks, input expectations | Unity/Unreal ✅ |
| **Online SDK** | `steam_api(64).dll`, `EOSSDK-Win64-Shipping.dll`, `Galaxy(64).dll` | which network **emulator** to drop in | Steam ✅ · Epic/Galaxy detect ✅, emu ⬜ |
| **DRM** | SteamStub `.bind` PE section | start the Steam client first | ✅ |
| **Architecture** | PE machine field | which proxy (x86/x64) | ✅ |
| **Single-instance** | mutex created early; second copy exits before a window | free the lock + retry (no per-game name) | ✅ (auto-recovery) |
| **Input API** | *not detected — covered universally* | the isolation layer hooks them all | XInput/RawInput/WGI ✅ · DInput ⬜ |

Design choice: we **do not try to detect the input API**. Detection there is
unreliable; instead the isolation layer covers *every* API at once, so the game can
use whatever it likes. That is the single biggest "no-config" lever.

## 3. Isolation — one proxy, every input API

A single injected DLL (per instance, per arch) makes the game see exactly **one**
physical controller and stay live in the background. This is the ProtoInput idea,
folded into one self-contained proxy we build and ship — no driver install.

Implemented today (`native/SplitRoast.XInputProxy/proxy.cpp`):

- **XInput** — replace `xinput1_3/1_4/9_1_0`, expose the assigned pad as index 0.
- **Raw Input** — claim one HID gamepad; drop foreign-pad `WM_INPUT` (data, buffered
  and message-pump paths); force `RIDEV_INPUTSINK` so the background window keeps
  receiving its pad.
- **Windows.Gaming.Input** — block `RoGetActivationFactory` for `Windows.Gaming.Input`.
- **Direct HID** — block `CreateFile` opens of foreign gamepad devices.
- **Focus** — fake `WM_ACTIVATE*`, swallow `WM_KILLFOCUS`, and spoof
  `GetForegroundWindow/GetActiveWindow/GetFocus` so engines don't pause/disable input
  in the background.
- **Window** — pin to the split region from inside the process; report the monitor as
  the split region so "fullscreen" fills the tile.
- **Cursor** — neutralise `ClipCursor` so the mouse isn't locked to one window.
- **Steam overlay** — refuse to load `GameOverlayRenderer*` so Steam Input can't
  re-hook XInput once the client is running.

**Gap → next: DirectInput.** Hook `DirectInput8Create` and wrap `IDirectInput8`:
filter `EnumDevices` to the assigned device and fail `CreateDevice` for foreign
pads (the same outcome x360ce/ViGEm reach with a driver, but in-process). This is
the one input API we don't yet isolate, and the user-visible gap for older/DInput
titles and DInput-mode pads.

## 4. Identity / network emulation — per platform

Two local copies must look like two different players so they can pair up and keep
separate saves. We mirror the folder and drop in the matching emulator with a
**distinct, stable id per instance**.

| Platform | Emulator | Status |
|----------|----------|--------|
| Steam | Goldberg / **gbe_fork** (bundled) | ✅ distinct SteamID64, LAN + relay settings |
| Epic (EOS) | Nemirtingas-style EOS emu | ⬜ detect ✅, integrate emu |
| GOG (Galaxy) | Nemirtingas-style Galaxy emu | ⬜ detect ✅, integrate emu |

**Limit (honest):** games that authenticate co-op against *their own* server with a
real platform web-ticket (NRFTW/MoonBackend → "Entitlement ticket invalid") can be
*launched* split-screen but may refuse a *shared* session, because the entitlement
is checked server-side. gbe_fork's `GetAuthTicketForWebApi` work helps some titles;
it can't satisfy a server we don't control. We detect & **explain** this instead of
looping on it.

## 5. Graceful degradation (replaces "try this handler")

When something can't be auto-handled, the engine picks the best automatic fallback
and writes a plain-English reason to the per-game diagnostics — never a config chore:

- Emulator for a detected SDK not bundled → launch isolated anyway; note co-op may
  be unavailable.
- Window not grabbed in time → the in-proxy region pin still places it.
- Second copy hits a single-instance lock → free it and retry automatically.
- A real edge case → a single optional **profile override** (exe path, isolate
  on/off), surfaced in the UI. One toggle, not a script.

## 6. Roadmap

- **P1 — Detection foundation** ✅ engine + multi-SDK (Steam/Epic/Galaxy) + DRM +
  arch, surfaced in diagnostics. *(this change)*
- **P2 — DirectInput isolation** ⬜ `dinput8` device filtering in the proxy →
  closes the last common input API.
- **P3 — Epic/Galaxy emulators** ⬜ wire Nemirtingas-style emus behind the existing
  `SteamEmulator`-shaped seam, chosen by `DetectedSdks`.
- **P4 — Emulator currency** ⬜ track gbe_fork releases (web-ticket auth) to widen
  which Steam titles can actually *pair*.
- **P5 — Capability cache + telemetry-free heuristics** ⬜ remember what worked per
  appid locally to make repeat launches instant and self-correcting.

## References

- Nucleus Co-op / SplitScreen.Me — handler model & generic handler:
  https://www.splitscreen.me/docs/ , https://github.com/SplitScreen-Me/splitscreenme-nucleus
- ProtoInput (input hooks) — https://github.com/Ilyaki/ProtoInput
- Goldberg / gbe_fork — https://github.com/Detanup01/gbe_fork
- Nemirtingas EOS/Galaxy emulators — https://github.com/Nemirtingas
