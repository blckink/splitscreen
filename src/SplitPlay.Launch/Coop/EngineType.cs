namespace SplitPlay.Launch.Coop;

/// <summary>
/// Game engine, detected from the install folder. Drives the default launch
/// arguments (e.g. how to ask for a borderless window of a given size).
/// </summary>
public enum EngineType
{
    Unknown,
    Unity,
    Unreal
}
