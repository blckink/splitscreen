using System;

namespace SplitRoast.Launch.Coop;

/// <summary>
/// The online/multiplayer back-end SDK(s) a game ships, detected from the DLLs in
/// its install folder. This is the axis that decides which network emulator a
/// second local instance needs so the two copies can see each other as a co-op
/// session without a real account/login:
///   - <see cref="Steam"/>   -&gt; Goldberg / gbe_fork (bundled today)
///   - <see cref="Epic"/>    -&gt; a Nemirtingas-style EOS emulator
///   - <see cref="Galaxy"/>  -&gt; a Nemirtingas-style GOG Galaxy emulator
///
/// A game can use more than one (hence [Flags]); we pick the emulator for whichever
/// SDK actually drives its lobby/identity. Detecting this - rather than asking the
/// user - is what lets the engine support many titles with no per-game handler.
/// </summary>
[Flags]
public enum OnlineSdk
{
    None = 0,
    Steam = 1 << 0,
    Epic = 1 << 1,
    Galaxy = 1 << 2
}
