# Redistributable components

## Steam emulator (gbe_fork)

Single-account local co-op uses the **gbe_fork** Steam emulator
(<https://github.com/Detanup01/gbe_fork>), a maintained fork of Goldberg
SteamEmu, which is licensed under the **LGPL-3.0**.

- The binaries are **not committed** to this repo. They are downloaded by
  `fetch-goldberg.ps1` (run by CI and bundled into the SplitPlay release) into
  `redist/goldberg/{x64,x86}/`.
- SplitPlay uses the emulator unmodified, as separate DLL files placed next to a
  mirrored copy of the game. This keeps it compliant with the LGPL (dynamic use,
  no modification, source available upstream).

To fetch it locally:

```powershell
powershell -ExecutionPolicy Bypass -File redist\fetch-goldberg.ps1
```

(Requires 7-Zip and internet access.)
