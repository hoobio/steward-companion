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
            return Normalise(new AppState([], []));
        }

        var json = File.ReadAllText(_path);
        var state = JsonSerializer.Deserialize(json, CompanionJsonContext.Default.AppState)
            ?? new AppState([], []);
        return SeedLegacyChannel(Normalise(state), _addonIds);
    }

    public static AppState RemoveInstall(AppState state, string flavourPath)
    {
        ArgumentNullException.ThrowIfNull(state);

        var prefix = Key(flavourPath, string.Empty);
        return state with
        {
            AddedInstalls = [.. state.AddedInstalls.Where(p => !string.Equals(p, flavourPath, StringComparison.OrdinalIgnoreCase))],
            Installs = state.Installs
                .Where(entry => !entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(entry => entry.Key, entry => entry.Value),
        };
    }

    private static AppState Normalise(AppState state) => state with
    {
        Channels = new Dictionary<string, string>(state.Channels ?? [], StringComparer.OrdinalIgnoreCase),
        Installs = state.Installs ?? [],
        AddedInstalls = state.AddedInstalls ?? [],
    };

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
