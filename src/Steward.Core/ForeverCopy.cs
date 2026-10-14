namespace Steward.Core;

public sealed record ForeverCopyPlan(IReadOnlyList<string> NewFolders, IReadOnlyList<string> UpdatedFolders, IReadOnlyList<string> SavedVariablesFiles, IReadOnlyList<string> SettingsFiles);

public sealed record ForeverCopyResult(int Copied, int Updated, int SavedVariablesFiles, int SettingsFiles, int Failures);

public static class ForeverCopy
{
    public static bool IsGated(WowInstall install, IEnumerable<WowInstall> installs, IEnumerable<string> done)
    {
        ArgumentNullException.ThrowIfNull(install);
        return install.ProductCode == ForeverLaunch.LiveProduct
            && !done.Contains(install.FlavourPath, StringComparer.OrdinalIgnoreCase)
            && installs.Any(other => other.ProductCode == ForeverLaunch.BetaProduct && !other.IsMissing);
    }

    public static ForeverCopyPlan Plan(WowInstall beta, WowInstall live, IEnumerable<string> managedFolders)
    {
        ArgumentNullException.ThrowIfNull(beta);
        ArgumentNullException.ThrowIfNull(live);
        var managed = new HashSet<string>(managedFolders, StringComparer.OrdinalIgnoreCase);
        var folders = Directory.Exists(beta.AddOnsPath)
            ? Directory.GetDirectories(beta.AddOnsPath).Select(Path.GetFileName).OfType<string>().ToList()
            : [];
        var accountFiles = NewerFiles(Account(beta), Account(live)).ToList();
        return new ForeverCopyPlan(
            [.. folders.Where(folder => !Directory.Exists(Path.Combine(live.AddOnsPath, folder)))],
            [.. folders.Where(folder => !managed.Contains(folder)
                && Directory.Exists(Path.Combine(live.AddOnsPath, folder))
                && NewerFiles(Path.Combine(beta.AddOnsPath, folder), Path.Combine(live.AddOnsPath, folder)).Any())],
            [.. accountFiles.Where(file => IsSavedVariables(file) && !file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))],
            [.. accountFiles.Where(file => !IsSavedVariables(file))]);
    }

    public static ForeverCopyResult Sync(WowInstall beta, WowInstall live, IEnumerable<string> managedFolders, bool addons, bool savedVariables, bool settings, Action<string, Exception> failed)
    {
        ArgumentNullException.ThrowIfNull(beta);
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(failed);
        var plan = Plan(beta, live, managedFolders);
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

        int CopyAccountFiles(IReadOnlyList<string> files)
        {
            var failedFiles = files.Count(file => !Attempt(Path.Combine(Account(live), file), failed, () => CopyFile(Path.Combine(Account(beta), file), Path.Combine(Account(live), file))));
            failures += failedFiles;
            return files.Count - failedFiles;
        }

        var savedVariablesFiles = savedVariables ? CopyAccountFiles(plan.SavedVariablesFiles) : 0;
        var settingsFiles = settings ? CopyAccountFiles(plan.SettingsFiles) : 0;
        return new ForeverCopyResult(copied, updated, savedVariablesFiles, settingsFiles, failures);
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

    private static bool IsSavedVariables(string relativePath) =>
        relativePath.Split(Path.DirectorySeparatorChar).SkipLast(1).Contains("SavedVariables", StringComparer.OrdinalIgnoreCase);

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
