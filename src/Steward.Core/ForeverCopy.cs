using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Steward.Core;

public sealed record ForeverCopyPlan(
    IReadOnlyList<string> NewFolders,
    IReadOnlyList<string> UpdatedFolders,
    IReadOnlyList<string> SavedVariablesFiles,
    IReadOnlyList<string> SettingsFiles,
    bool SettingsNewer,
    IReadOnlyList<string> ClientSettings,
    bool ClientSettingsNewer,
    bool LiveConfigMissing,
    IReadOnlyList<ForeverCopyAccount> BetaAccounts,
    IReadOnlyList<ForeverCopyAccount> LiveAccounts,
    IReadOnlyDictionary<string, string?> AccountMap);

public sealed record ForeverCopyAccount(string Folder, IReadOnlyList<string> Characters, string? FoundIn = null);

public sealed record ForeverCopyResult(int Copied, int Updated, int SavedVariablesFiles, int SettingsFiles, int ClientSettings, int Failures);

public static partial class ForeverCopy
{
    private static readonly string[] ClientSettingPrefixes = ["graphics", "raidGraphics", "RAID", "Sound_", "Gx", "maxFPS", "shadow", "lodObject", "accessibility"];

    private static readonly HashSet<string> ClientSettingNames = new(
    [
        "showScreenNarrationDialog", "Brightness", "Contrast", "Gamma", "componentTextureLevel", "DepthBasedOpacity", "doodadLodScale", "entityLodDist",
        "entityShadowFadeScale", "farclip", "ffxAntiAliasingMode", "groundEffectDensity", "groundEffectDist", "horizonClip",
        "horizonStart", "LowLatencyMode", "MSAAAlphaTest", "MSAAQuality", "OutlineEngineMode", "particleDensity",
        "particleMTDensity", "physicsLevel", "projectedTextures", "reflectionMode", "refraction", "RenderFormat", "renderScale",
        "ResampleQuality", "ResampleSharpness", "rippleDetail", "spellClutter", "spellVisualDensityFilterSetting", "SSAO",
        "sunShafts", "targetFPS", "terrainLodDist", "TerrainLodDiv", "terrainMipLevel", "textureFilteringMode", "useMaxFPS",
        "useMaxFPSBk", "useTargetFPS", "volumeFogLevel", "vsync", "waterDetail", "weatherDensity", "wmoLodDist", "worldBaseMip",
    ], StringComparer.OrdinalIgnoreCase);

    // Slots in an account's cache.md5 (24 bytes each: MD5, save time, padding); at login the client downloads the server's copy whenever the slot time differs from the server's, later or earlier, so only the MD5 is rewritten.
    private static readonly Dictionary<string, int> AccountDataSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        ["config-cache.wtf"] = 0,
        ["bindings-cache.wtf"] = 1,
        ["macros-cache.txt"] = 2,
        ["tts-cache-account.txt"] = 3,
        ["edit-mode-cache-account.txt"] = 5,
    };

    public static bool IsGated(WowInstall install, IEnumerable<WowInstall> installs, IEnumerable<string> done)
    {
        ArgumentNullException.ThrowIfNull(install);
        return install.ProductCode == ForeverLaunch.LiveProduct
            && !done.Contains(install.FlavourPath, StringComparer.OrdinalIgnoreCase)
            && installs.Any(other => other.ProductCode == ForeverLaunch.BetaProduct && !other.IsMissing);
    }

    public static IReadOnlyDictionary<string, string?> DefaultAccountMap(WowInstall beta, WowInstall live)
    {
        ArgumentNullException.ThrowIfNull(beta);
        ArgumentNullException.ThrowIfNull(live);
        return DefaultMap(Accounts(beta), LiveAccounts(beta, live), StaleAccounts(live));
    }

    public static bool IsLegacyAccountName(string folder) => !NumberedAccount().IsMatch(folder);

    public static string? FirstLegacyAccount(WowInstall live) =>
        Accounts(live).Select(account => account.Folder).FirstOrDefault(IsLegacyAccountName);

    public static bool NeedsAccountMigration(WowInstall beta, WowInstall live)
    {
        ArgumentNullException.ThrowIfNull(beta);
        ArgumentNullException.ThrowIfNull(live);
        var stale = StaleAccounts(live);
        return stale.Count > 0 && Accounts(beta).Any(account => stale.Contains(account.Folder));
    }

    // An account-number folder with no realm folder, beside a folder named after the old WoW account, is one an earlier copy created and the live client never reads.
    private static HashSet<string> StaleAccounts(WowInstall live)
    {
        var accounts = Accounts(live);
        return accounts.Any(account => IsLegacyAccountName(account.Folder))
            ? accounts.Where(account => !IsLegacyAccountName(account.Folder) && !HasRealmFolder(Path.Combine(Account(live), account.Folder)))
                .Select(account => account.Folder)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasRealmFolder(string account) =>
        Directory.EnumerateDirectories(account).Any(realm => !string.Equals(Path.GetFileName(realm), "SavedVariables", StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"^\d+#\d+$")]
    private static partial Regex NumberedAccount();

    private static Dictionary<string, string?> DefaultMap(List<ForeverCopyAccount> betaAccounts, List<ForeverCopyAccount> liveAccounts, HashSet<string> stale)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in betaAccounts)
        {
            map[account.Folder] = liveAccounts.Select(live => live.Folder).FirstOrDefault(live => !stale.Contains(live) && string.Equals(live, account.Folder, StringComparison.OrdinalIgnoreCase));
        }

        var unmapped = map.Where(entry => entry.Value is null).Select(entry => entry.Key).ToList();
        var free = liveAccounts.Select(live => live.Folder).Where(live => !stale.Contains(live) && !map.Values.Contains(live, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unmapped.Count == 1 && free.Count == 1)
        {
            map[unmapped[0]] = free[0];
        }
        else if (free.Where(IsLegacyAccountName).ToList() is [var legacy]
            && unmapped.FirstOrDefault(account => account.EndsWith("#1", StringComparison.Ordinal)) is { } first)
        {
            map[first] = legacy;
        }

        return map;
    }

    public static ForeverCopyPlan Plan(WowInstall beta, WowInstall live, IEnumerable<string> managedFolders, IReadOnlyDictionary<string, string?>? accountMap = null)
    {
        ArgumentNullException.ThrowIfNull(beta);
        ArgumentNullException.ThrowIfNull(live);
        var managed = new HashSet<string>(managedFolders, StringComparer.OrdinalIgnoreCase);
        var folders = Directory.Exists(beta.AddOnsPath)
            ? Directory.GetDirectories(beta.AddOnsPath).Select(Path.GetFileName).OfType<string>().ToList()
            : [];
        var betaAccounts = Accounts(beta);
        var liveAccounts = LiveAccounts(beta, live);
        var requested = accountMap ?? DefaultMap(betaAccounts, liveAccounts, StaleAccounts(live));
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in betaAccounts)
        {
            var target = requested.GetValueOrDefault(account.Folder);
            map[account.Folder] = liveAccounts.Select(liveAccount => liveAccount.Folder).FirstOrDefault(liveFolder => string.Equals(liveFolder, target, StringComparison.OrdinalIgnoreCase));
        }

        var accountFiles = Directory.Exists(Account(beta))
            ? Directory.EnumerateFiles(Account(beta), "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(Account(beta), file))
                .Where(IsAccountLevel)
                .Where(file => LiveAccountFile(live, map, file) is { } target && IsNewer(Path.Combine(Account(beta), file), target))
                .ToList()
            : [];
        var liveConfigMissing = !File.Exists(Config(live));
        var settingsFiles = DifferingSettingsFiles(beta, live, betaAccounts, map).ToList();
        return new ForeverCopyPlan(
            [.. folders.Where(folder => !Directory.Exists(Path.Combine(live.AddOnsPath, folder)))],
            [.. folders.Where(folder => !managed.Contains(folder)
                && Directory.Exists(Path.Combine(live.AddOnsPath, folder))
                && NewerFiles(Path.Combine(beta.AddOnsPath, folder), Path.Combine(live.AddOnsPath, folder)).Any())],
            [.. accountFiles.Where(file => IsSavedVariables(file) && !file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) && !IsStewardCharacters(file))],
            settingsFiles,
            settingsFiles.Any(file => IsNewer(Path.Combine(Account(beta), file), LiveAccountFile(live, map, file)!)),
            liveConfigMissing ? [] : [.. DifferingClientSettings(beta, live).Select(setting => setting.Name)],
            IsNewer(Config(beta), Config(live)),
            liveConfigMissing,
            betaAccounts,
            liveAccounts,
            map);
    }

    public static ForeverCopyResult Sync(WowInstall beta, WowInstall live, IEnumerable<string> managedFolders, bool addons, bool savedVariables, bool settings, bool clientSettings, Action<string, Exception> failed, IReadOnlyDictionary<string, string?>? accountMap = null)
    {
        ArgumentNullException.ThrowIfNull(beta);
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(failed);
        var plan = Plan(beta, live, managedFolders, accountMap);
        int copied = 0, updated = 0, failures = 0;
        if (addons)
        {
            lock (AddonUpdater.AddOnsWriteLock)
            {
                foreach (var folder in plan.NewFolders)
                {
                    if (Attempt(Path.Combine(live.AddOnsPath, folder), failed, () => CopyNewFolder(Path.Combine(beta.AddOnsPath, folder), Path.Combine(live.AddOnsPath, folder))))
                    {
                        copied++;
                    }
                    else
                    {
                        failures++;
                    }
                }

                foreach (var folder in plan.UpdatedFolders)
                {
                    var source = Path.Combine(beta.AddOnsPath, folder);
                    var target = Path.Combine(live.AddOnsPath, folder);
                    var files = NewerFiles(source, target).ToList();
                    var failedFiles = files.Count(file => !Attempt(Path.Combine(target, file), failed, () => CopyFile(Path.Combine(source, file), Path.Combine(target, file))));
                    failures += failedFiles;
                    if (failedFiles < files.Count)
                    {
                        updated++;
                    }
                }
            }
        }

        int CopyAccountFiles(IReadOnlyList<string> files, bool accountData)
        {
            var failedFiles = files.Count(file =>
            {
                var target = LiveAccountFile(live, plan.AccountMap, file)!;
                return !Attempt(target, failed, () =>
                {
                    CopyFile(Path.Combine(Account(beta), file), target);
                    if (accountData)
                    {
                        StampAccountData(target);
                    }
                });
            });
            failures += failedFiles;
            return files.Count - failedFiles;
        }

        var savedVariablesFiles = savedVariables ? CopyAccountFiles(plan.SavedVariablesFiles, accountData: false) : 0;
        var settingsFiles = settings ? CopyAccountFiles(plan.SettingsFiles, accountData: true) : 0;
        var clientSettingsCopied = 0;
        if (clientSettings && plan.ClientSettings.Count > 0 && !Attempt(Config(live), failed, () => clientSettingsCopied = MergeClientSettings(beta, live)))
        {
            failures++;
        }

        return new ForeverCopyResult(copied, updated, savedVariablesFiles, settingsFiles, clientSettingsCopied, failures);
    }

    // Config.wtf also holds portal, locale and cache counters for its own client, so only video and sound CVars cross over.
    private static bool IsClientSetting(string name) =>
        ClientSettingNames.Contains(name) || ClientSettingPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static (string Name, string Value)? ParseSetting(string line)
    {
        var parts = line.Split(' ', 3);
        return parts is ["SET", var name, var value] && value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? (name, value[1..^1])
            : null;
    }

    private static Dictionary<string, string> ReadSettings(string path)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(path))
        {
            foreach (var setting in File.ReadLines(path).Select(ParseSetting).OfType<(string Name, string Value)>())
            {
                settings[setting.Name] = setting.Value;
            }
        }

        return settings;
    }

    private static List<(string Name, string Value)> DifferingClientSettings(WowInstall beta, WowInstall live)
    {
        var liveSettings = ReadSettings(Config(live));
        return [.. ReadSettings(Config(beta))
            .Where(setting => IsClientSetting(setting.Key)
                && !(liveSettings.TryGetValue(setting.Key, out var current) && current == setting.Value))
            .Select(setting => (setting.Key, setting.Value))];
    }

    private static int MergeClientSettings(WowInstall beta, WowInstall live)
    {
        var changes = DifferingClientSettings(beta, live).ToDictionary(setting => setting.Name, setting => setting.Value, StringComparer.OrdinalIgnoreCase);
        var path = Config(live);
        var lines = File.ReadAllLines(path).ToList();
        var replaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < lines.Count; i++)
        {
            if (ParseSetting(lines[i]) is { } setting && changes.TryGetValue(setting.Name, out var value))
            {
                lines[i] = $"SET {setting.Name} \"{value}\"";
                replaced.Add(setting.Name);
            }
        }

        lines.AddRange(changes.Where(change => !replaced.Contains(change.Key)).Select(change => $"SET {change.Key} \"{change.Value}\""));
        var temp = path + ".steward-copy";
        File.WriteAllText(temp, string.Concat(lines.Select(line => line + "\r\n")));
        File.Move(temp, path, overwrite: true);
        return changes.Count;
    }

    public static AppState CarryState(AppState state, string betaPath, string livePath, int? liveVersionType, Func<string, bool> isOnLive, Func<string, string?> catalogueFolder)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(isOnLive);
        ArgumentNullException.ThrowIfNull(catalogueFolder);
        var betaPrefix = AppStateStore.Key(betaPath, string.Empty);

        var providers = new Dictionary<string, List<ProviderAddonRecord>>(state.ProviderAddons ?? [], StringComparer.OrdinalIgnoreCase);
        var liveRecords = (providers.GetValueOrDefault(livePath) ?? []).ToList();
        liveRecords.AddRange((providers.GetValueOrDefault(betaPath) ?? []).Where(record =>
            record.VersionType == liveVersionType
            && isOnLive(record.FolderName)
            && !liveRecords.Any(existing => string.Equals(existing.Id, record.Id, StringComparison.OrdinalIgnoreCase))));
        if (liveRecords.Count > 0)
        {
            providers[livePath] = liveRecords;
        }

        string? FolderOf(string id) =>
            liveRecords.FirstOrDefault(record => string.Equals(record.Id, id, StringComparison.OrdinalIgnoreCase))?.FolderName
            ?? (id.StartsWith("curseforge-", StringComparison.OrdinalIgnoreCase) ? null : catalogueFolder(id));

        bool AddonOnLive(string id) => FolderOf(id) is { } folder && isOnLive(folder);

        var installs = new Dictionary<string, InstalledAddonRecord>(state.Installs ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var (key, record) in (state.Installs ?? []).Where(entry => entry.Key.StartsWith(betaPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            var id = key[betaPrefix.Length..];
            if (AddonOnLive(id))
            {
                installs.TryAdd(AppStateStore.Key(livePath, id), record);
            }
        }

        return state with
        {
            ProviderAddons = providers,
            Installs = installs,
            IgnoredAddons = Carried(state.IgnoredAddons, betaPrefix, livePath, AddonOnLive),
            KeptLocalAddons = Carried(state.KeptLocalAddons, betaPrefix, livePath, isOnLive),
        };
    }

    private static List<string> Carried(List<string>? keys, string betaPrefix, string livePath, Func<string, bool> onLive)
    {
        var result = (keys ?? []).ToList();
        foreach (var key in keys ?? [])
        {
            if (key.StartsWith(betaPrefix, StringComparison.OrdinalIgnoreCase)
                && onLive(key[betaPrefix.Length..])
                && AppStateStore.Key(livePath, key[betaPrefix.Length..]) is var liveKey
                && !result.Contains(liveKey, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(liveKey);
            }
        }

        return result;
    }

    private static string Account(WowInstall install) => Path.Combine(install.FlavourPath, "WTF", "Account");

    private static string Config(WowInstall install) => Path.Combine(install.FlavourPath, "WTF", "Config.wtf");

    // Realm and character folders are named after the beta's realms and characters, which live does not share.
    private static bool IsAccountLevel(string relativePath) =>
        relativePath.Split(Path.DirectorySeparatorChar) switch
        {
            [_, _] => true,
            [_, var folder, _] => string.Equals(folder, "SavedVariables", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    private static bool IsSavedVariables(string relativePath) =>
        relativePath.Split(Path.DirectorySeparatorChar).Length == 3;

    // Steward.lua holds the beta's characters, which a live install would then push as its own.
    private static bool IsStewardCharacters(string relativePath) =>
        string.Equals(Path.GetFileName(relativePath), "Steward.lua", StringComparison.OrdinalIgnoreCase);

    // The client rewrites these files at every login, so their dates say nothing and only a content difference is offered.
    private static IEnumerable<string> DifferingSettingsFiles(WowInstall beta, WowInstall live, List<ForeverCopyAccount> betaAccounts, Dictionary<string, string?> map) =>
        betaAccounts
            .SelectMany(account => AccountDataSlots.Keys.Select(file => Path.Combine(account.Folder, file)))
            .Where(file => File.Exists(Path.Combine(Account(beta), file))
                && LiveAccountFile(live, map, file) is { } target
                && !SameContents(Path.Combine(Account(beta), file), target));

    private static string? LiveAccountFile(WowInstall live, IReadOnlyDictionary<string, string?> map, string relativePath)
    {
        var split = relativePath.IndexOf(Path.DirectorySeparatorChar);
        return map.GetValueOrDefault(relativePath[..split]) is { } mapped ? Path.Combine(Account(live), mapped, relativePath[(split + 1)..]) : null;
    }

    private static List<ForeverCopyAccount> Accounts(WowInstall install) =>
        Directory.Exists(Account(install))
            ? [.. Directory.EnumerateDirectories(Account(install))
                .Where(account => !string.Equals(Path.GetFileName(account), "SavedVariables", StringComparison.OrdinalIgnoreCase))
                .Select(account => new ForeverCopyAccount(Path.GetFileName(account), CharacterNames(account)))
                .OrderBy(account => account.Folder, StringComparer.OrdinalIgnoreCase)]
            : [];

    private static List<ForeverCopyAccount> LiveAccounts(WowInstall beta, WowInstall live)
    {
        var accounts = Accounts(live);
        return accounts.Any(account => IsLegacyAccountName(account.Folder)) ? accounts : [.. accounts, .. Candidates(beta, live, accounts)];
    }

    // Forever creates a classic-named account folder only at the first login, but the other game versions in the install root often have it already.
    private static IEnumerable<ForeverCopyAccount> Candidates(WowInstall beta, WowInstall live, List<ForeverCopyAccount> accounts)
    {
        var root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(live.FlavourPath));
        if (root is null || !Directory.Exists(root))
        {
            yield break;
        }

        var seen = new HashSet<string>(accounts.Select(account => account.Folder), StringComparer.OrdinalIgnoreCase);
        foreach (var sibling in Directory.EnumerateDirectories(root).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (SamePath(sibling, live.FlavourPath) || SamePath(sibling, beta.FlavourPath) || !Directory.Exists(Path.Combine(sibling, "WTF", "Account")))
            {
                continue;
            }

            foreach (var folder in Directory.EnumerateDirectories(Path.Combine(sibling, "WTF", "Account")).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase))
            {
                if (IsLegacyAccountName(folder) && !string.Equals(folder, "SavedVariables", StringComparison.OrdinalIgnoreCase) && seen.Add(folder))
                {
                    yield return new ForeverCopyAccount(folder, [], Path.GetFileName(sibling));
                }
            }
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    private static List<string> CharacterNames(string account) =>
        [.. Directory.EnumerateDirectories(account)
            .Where(realm => !string.Equals(Path.GetFileName(realm), "SavedVariables", StringComparison.OrdinalIgnoreCase))
            .SelectMany(realm => Directory.EnumerateDirectories(realm))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];

    private static bool SameContents(string source, string target) =>
        File.Exists(target) && File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(target));

    [SuppressMessage("Security", "CA5351", Justification = "cache.md5 is the WoW client's own format, which holds MD5 hashes")]
    private static void StampAccountData(string file)
    {
        var path = Path.Combine(Path.GetDirectoryName(file)!, "cache.md5");
        var slots = File.Exists(path) ? File.ReadAllBytes(path) : [];
        if (slots.Length < 240)
        {
            Array.Resize(ref slots, 240);
        }

        MD5.HashData(File.ReadAllBytes(file)).CopyTo(slots, AccountDataSlots[Path.GetFileName(file)] * 24);
        var temp = path + ".steward-copy";
        File.WriteAllBytes(temp, slots);
        File.Move(temp, path, overwrite: true);
    }

    private static IEnumerable<string> NewerFiles(string sourceRoot, string targetRoot) =>
        Directory.Exists(sourceRoot)
            ? Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(sourceRoot, file))
                .Where(file => IsNewer(Path.Combine(sourceRoot, file), Path.Combine(targetRoot, file)))
            : [];

    private static bool IsNewer(string source, string target) =>
        !File.Exists(target) || File.GetLastWriteTimeUtc(target) < File.GetLastWriteTimeUtc(source);

    private static void CopyFile(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
    }

    private static void CopyNewFolder(string source, string target)
    {
        var staging = target + ".steward-copy";
        try
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                CopyFile(file, Path.Combine(staging, Path.GetRelativePath(source, file)));
            }

            Directory.CreateDirectory(staging);
            Directory.Move(staging, target);
        }
        catch
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            throw;
        }
    }

    private static bool Attempt(string path, Action<string, Exception> failed, Action copy)
    {
        try
        {
            copy();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failed(path, ex);
            return false;
        }
    }
}
