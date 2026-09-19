using System.Text.Json;

namespace Steward.Core;

public sealed class AppStateStore
{
    private readonly IReadOnlyList<string> _addonIds;
    private readonly string _path;

    public AppStateStore(IReadOnlyList<string> addonIds, string? path = null)
    {
        _addonIds = addonIds;
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Steward",
            "state.json");
    }

    public static string Key(string flavourPath, string addonId) => $"{flavourPath}|{addonId}";

    public AppState Load()
    {
        if (!File.Exists(_path))
        {
            return new AppState([], []);
        }

        var json = File.ReadAllText(_path);
        var state = JsonSerializer.Deserialize(json, CompanionJsonContext.Default.AppState)
            ?? new AppState([], []);
        var normalised = state with
        {
            Channels = new Dictionary<string, string>(state.Channels ?? [], StringComparer.OrdinalIgnoreCase),
            Installs = state.Installs ?? [],
        };
        return SeedLegacyChannel(normalised, _addonIds);
    }

    internal static AppState SeedLegacyChannel(AppState state, IReadOnlyList<string> addonIds)
    {
        if (state.LegacyChannel is null)
        {
            return state;
        }

        var channels = new Dictionary<string, string>(state.Channels, StringComparer.OrdinalIgnoreCase);
        foreach (var addonId in addonIds)
        {
            channels.TryAdd(addonId, state.LegacyChannel);
        }

        return state with { Channels = channels, LegacyChannel = null };
    }

    public void Save(AppState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(state, CompanionJsonContext.Default.AppState));
    }
}
