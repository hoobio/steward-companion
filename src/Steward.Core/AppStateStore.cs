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

    public static string CharacterSyncKey(string guildId, string flavourPath) => $"{guildId}|{flavourPath}";

    public static AutoUpdateMode ParseAutoUpdate(string? value) => value switch
    {
        "always" => AutoUpdateMode.Always,
        "never" => AutoUpdateMode.Never,
        _ => AutoUpdateMode.OutOfGame,
    };

    private string CharacterSyncPath => Path.Combine(Path.GetDirectoryName(_path)!, "character_sync.json");

    public AppState Load() => Normalise(WithCharacterSync(LoadState()));

    private AppState LoadState()
    {
        if (!File.Exists(_path))
        {
            return Normalise(new AppState([], []));
        }

        try
        {
            var state = JsonSerializer.Deserialize(File.ReadAllText(_path), CompanionJsonContext.Default.AppState)
                ?? new AppState([], []);
            return MigrateDevelopChannel(SeedLegacyChannel(Normalise(state), _addonIds));
        }
        catch (JsonException)
        {
            return Normalise(new AppState([], []));
        }
    }

    private AppState WithCharacterSync(AppState state)
    {
        if (!File.Exists(CharacterSyncPath))
        {
            return state;
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(CharacterSyncPath), CompanionJsonContext.Default.CharacterSyncState) is { } sync
                ? state with { CharacterSync = sync.CharacterSync, CharacterSyncBatches = sync.CharacterSyncBatches }
                : state;
        }
        catch (JsonException)
        {
            return state;
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
            RestedXpGuidesGeneration = (state.RestedXpGuidesGeneration ?? [])
                .Where(entry => !string.Equals(entry.Key, flavourPath, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase),
            GuildRosterSync = (state.GuildRosterSync ?? [])
                .Where(entry => !string.Equals(entry.Key, flavourPath, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase),
            CharacterSync = (state.CharacterSync ?? [])
                .Where(entry => !entry.Key.EndsWith("|" + flavourPath, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase),
            CharacterSyncBatches = (state.CharacterSyncBatches ?? [])
                .Where(entry => !entry.Key.EndsWith("|" + flavourPath, StringComparison.OrdinalIgnoreCase))
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
        RestedXpGuidesGeneration = new Dictionary<string, long>(state.RestedXpGuidesGeneration ?? [], StringComparer.OrdinalIgnoreCase),
        GuildRosterSync = new Dictionary<string, string>(state.GuildRosterSync ?? [], StringComparer.OrdinalIgnoreCase),
        CharacterSync = new Dictionary<string, CharacterPushRecord>(state.CharacterSync ?? [], StringComparer.OrdinalIgnoreCase),
        CharacterSyncBatches = new Dictionary<string, CharacterSyncBatch>(state.CharacterSyncBatches ?? [], StringComparer.OrdinalIgnoreCase),
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

    internal static AppState MigrateDevelopChannel(AppState state)
    {
        if (!state.Channels.Values.Any(IsRemovedDevelopChannel))
        {
            return state;
        }

        var channels = new Dictionary<string, string>(state.Channels, StringComparer.OrdinalIgnoreCase);
        foreach (var addonId in channels.Keys.ToList())
        {
            if (IsRemovedDevelopChannel(channels[addonId]))
            {
                channels[addonId] = "pre-release";
            }
        }

        return state with { Channels = channels };
    }

    private static bool IsRemovedDevelopChannel(string channel) =>
        string.Equals(channel, "develop", StringComparison.OrdinalIgnoreCase)
        || string.Equals(channel, "development", StringComparison.OrdinalIgnoreCase);

    public void Save(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // Kept out of state.json: an older build sharing that file rewrites it without the keys it does not know.
        WriteAtomically(CharacterSyncPath, JsonSerializer.Serialize(
            new CharacterSyncState(state.CharacterSync ?? [], state.CharacterSyncBatches ?? []),
            CompanionJsonContext.Default.CharacterSyncState));
        WriteAtomically(_path, JsonSerializer.Serialize(
            state with { CharacterSync = null!, CharacterSyncBatches = null! },
            CompanionJsonContext.Default.AppState));
    }

    private static void WriteAtomically(string path, string contents)
    {
        var temporaryPath = $"{path}.tmp";
        File.WriteAllText(temporaryPath, contents);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
