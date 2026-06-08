using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SplitPlay.Core.Abstractions;
using SplitPlay.Core.Models;

namespace SplitPlay.App.Services;

/// <summary>
/// Stores each game's <see cref="GameProfile"/> as a small JSON file under
/// %AppData%\SplitPlay\profiles. One file per app id keeps things simple, easy to
/// inspect and resilient (a corrupt profile never affects the others).
/// </summary>
public sealed class JsonGameProfileStore : IGameProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly string _profilesDir;

    public JsonGameProfileStore()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _profilesDir = Path.Combine(appData, "SplitPlay", "profiles");
        Directory.CreateDirectory(_profilesDir);
    }

    public async Task<GameProfile> LoadAsync(uint appId, CancellationToken cancellationToken = default)
    {
        string path = GetPath(appId);
        if (!File.Exists(path))
        {
            return GameProfile.CreateDefault(appId);
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            GameProfile? profile =
                await JsonSerializer.DeserializeAsync<GameProfile>(stream, JsonOptions, cancellationToken);

            // Guard against an empty/garbled file and a too-short assignments array.
            if (profile is null)
            {
                return GameProfile.CreateDefault(appId);
            }

            if (profile.ControllerAssignments.Length < 2)
            {
                profile.ControllerAssignments = new int?[2];
            }

            return profile;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Never let a bad profile crash the app; fall back to defaults.
            return GameProfile.CreateDefault(appId);
        }
    }

    public async Task SaveAsync(GameProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // Write to a temp file then move into place so a crash mid-write cannot
        // leave a half-written profile behind.
        string path = GetPath(profile.AppId);
        string tempPath = path + ".tmp";

        await using (FileStream stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private string GetPath(uint appId) => Path.Combine(_profilesDir, $"{appId}.json");
}
