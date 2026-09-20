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

        try
        {
            var state = JsonSerializer.Deserialize(File.ReadAllText(_path), CompanionJsonContext.Default.AppState)
                ?? new AppState([], []);
            return SeedLegacyChannel(Normalise(state), _addonIds);
        }
        catch (JsonException)
        {
            return Normalise(new AppState([], []));
        }
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
            RestedXpGuides = (state.RestedXpGuides ?? [])
                .Where(entry => !entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase),
            RestedXpGuideChoices = (state.RestedXpGuideChoices ?? [])
                .Where(entry => !string.Equals(entry.Key, flavourPath, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase),
        };
    }

    private static AppState Normalise(AppState state) => state with
    {
        Channels = new Dictionary<string, string>(state.Channels ?? [], StringComparer.OrdinalIgnoreCase),
        Installs = new Dictionary<string, InstalledAddonRecord>(state.Installs ?? [], StringComparer.OrdinalIgnoreCase),
        AddedInstalls = state.AddedInstalls ?? [],
        HiddenAddons = state.HiddenAddons ?? [],
        RestedXpGuides = new Dictionary<string, RestedXpGuideRecord>(state.RestedXpGuides ?? [], StringComparer.OrdinalIgnoreCase),
        RestedXpGuideChoices = MergeGuideChoices(state),
        LegacyRestedXpGuideChoice = null,
    };

    private static Dictionary<string, List<string>> MergeGuideChoices(AppState state)
    {
        var choices = new Dictionary<string, List<string>>(state.RestedXpGuideChoices ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var entry in state.LegacyRestedXpGuideChoice ?? [])
        {
            choices.TryAdd(entry.Key, [entry.Value]);
        }

        return choices;
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
        var temporaryPath = $"{_path}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, CompanionJsonContext.Default.AppState));
        File.Move(temporaryPath, _path, overwrite: true);
    }
}
