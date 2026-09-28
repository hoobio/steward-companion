using System.Text.Json;

using Steward.Core;
using Steward.Core.Diagnostics;

namespace Steward.App.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly TimeSpan CurseForgeCheckInterval = TimeSpan.FromMinutes(30);

    private DateTimeOffset _lastCurseForgeCheck;

    public int? CurseForgeVersionType(WowInstall install) =>
        install?.ProductCode is { } product && _curseForgeVersionTypes.TryGetValue(product, out var versionType) ? versionType : null;

    private List<ProviderAddonRecord> ProviderRecords(string flavourPath) =>
        _stateStore.Load().ProviderAddons.GetValueOrDefault(flavourPath) ?? [];

    private IReadOnlyList<ManagedAddon> ProviderAddons(string flavourPath) => HasCurseForgeFeature
        ? [.. ProviderRecords(flavourPath)
            .Where(record => record.Source == CurseForgeAddons.Source)
            .OrderBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
            .Select(record => CurseForgeAddons.ToManagedAddon(record, _gigagrugClient.CurseForgeManifestBaseUrl(record.ModId, record.VersionType)))]
        : [];

    private IReadOnlyList<ManagedAddon> AddonsFor(string flavourPath) => [.. VisibleAddons(), .. ProviderAddons(flavourPath)];

    private IReadOnlyList<ManagedAddon> AllProviderAddons() =>
        [.. Installs.SelectMany(install => ProviderAddons(install.FlavourPath)).DistinctBy(addon => addon.Id, StringComparer.OrdinalIgnoreCase)];

    private IReadOnlyList<ManagedAddon> AllVisibleAddons() => [.. VisibleAddons(), .. AllProviderAddons()];

    private IReadOnlyList<string> ExcludedFolders(WowInstallViewModel install) =>
    [
        .. _addons.Select(addon => addon.FolderName),
        StewardGuidesAddon.FolderName,
        .. ProviderRecords(install.FlavourPath).SelectMany(CurseForgeAddons.Folders),
    ];

    private async Task<IReadOnlyList<ProviderAddonRecord>> IdentifyCurseForgeAsync(WowInstallViewModel install, IReadOnlyList<LocalAddon> scanned)
    {
        if (CurseForgeVersionType(install.Install) is not { } versionType || scanned.Count == 0)
        {
            return [];
        }

        try
        {
            var folders = scanned.SelectMany(addon => addon.FoldedFolders.Prepend(addon.FolderName)).ToList();
            var request = await Task.Run(() => CurseForgeAddons.MatchRequest(install.AddOnsPath, folders)).ConfigureAwait(false);
            var matches = await _gigagrugClient.MatchCurseForgeAsync(versionType, request, CancellationToken.None).ConfigureAwait(false);
            var titles = scanned.ToDictionary(addon => addon.FolderName, addon => addon.Name, StringComparer.OrdinalIgnoreCase);
            var adopted = CurseForgeAddons.Adopt(
                matches,
                versionType,
                folder => Directory.Exists(Path.Combine(install.AddOnsPath, folder)),
                folder => titles.GetValueOrDefault(folder));

            var withIcons = new List<ProviderAddonRecord>(adopted.Count);
            foreach (var record in adopted)
            {
                withIcons.Add(record with { IconUrl = await _gigagrugClient.GetCurseForgeIconUrlAsync(record.ModId, versionType, CancellationToken.None).ConfigureAwait(false) });
            }

            _logger.Info($"CurseForge match on {install.FlavourPath}: {folders.Count} folder(s) sent, {request.Declared.Count} declared, {matches.Count} match(es), {withIcons.Count} addon(s) identified");
            return withIcons;
        }
        catch (Exception ex) when (ex is HttpRequestException or GigagrugRequestException or SessionExpiredException or TaskCanceledException or JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.Warn(ex, $"CurseForge match failed on {install.FlavourPath}");
            return [];
        }
    }

    private bool ReconcileProviderAddons(WowInstallViewModel install, IReadOnlyList<ProviderAddonRecord> identified)
    {
        var state = _stateStore.Load();
        var records = state.ProviderAddons.GetValueOrDefault(install.FlavourPath) ?? [];
        var busy = install.AddonRows.Where(row => row.IsBusy).Select(row => row.AddonId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept = records.Where(record => busy.Contains(record.Id) || Directory.Exists(Path.Combine(install.AddOnsPath, record.FolderName))).ToList();
        var added = identified.Where(record => !kept.Any(existing => string.Equals(existing.Id, record.Id, StringComparison.OrdinalIgnoreCase))).ToList();
        if (kept.Count == records.Count && added.Count == 0)
        {
            return false;
        }

        foreach (var gone in records.Except(kept))
        {
            state.Installs.Remove(AppStateStore.Key(install.FlavourPath, gone.Id));
            _logger.Info($"CurseForge addon {gone.Id} dropped from {install.FlavourPath}: {gone.FolderName} is gone");
        }

        SaveProviderRecords(state, install.FlavourPath, [.. kept, .. added]);
        SyncProviderRows(install);
        if (added.Count > 0 && HasCurseForgeFeature)
        {
            _ = ProbeAddedProviderAddonsAsync(ProviderAddons(install.FlavourPath).Where(addon => added.Any(record => record.Id == addon.Id)).ToList());
        }

        return added.Count > 0;
    }

    private void SaveProviderRecords(AppState state, string flavourPath, List<ProviderAddonRecord> records)
    {
        if (records.Count == 0)
        {
            state.ProviderAddons.Remove(flavourPath);
        }
        else
        {
            state.ProviderAddons[flavourPath] = records;
        }

        _stateStore.Save(state);
    }

    private void SyncProviderRows(WowInstallViewModel install)
    {
        install.SyncAddons(AddonsFor(install.FlavourPath));
        install.SetIsAdmin(IsAuthorized);
        install.ApplyStatus(_status, background: false);
        RebuildAddonChannels();
        RecomputeSummary();
    }

    private async Task ProbeAddedProviderAddonsAsync(IReadOnlyList<ManagedAddon> addons)
    {
        if (await ProbeProviderAddonsAsync(addons).ConfigureAwait(true))
        {
            ApplyStatus(background: false);
            RecomputeSummary();
        }
    }

    private async Task<bool> ProbeProviderAddonsAsync(IReadOnlyList<ManagedAddon> addons)
    {
        try
        {
            var state = _stateStore.Load();
            foreach (var addon in addons)
            {
                var releases = await _addonUpdater.ProbeChannelsAsync(addon, VisibleChannels, CancellationToken.None).ConfigureAwait(true);
                _releases[addon.Id] = releases;
                _status[addon.Id] = AddonChannelStatus.Resolve(state.Channels.GetValueOrDefault(addon.Id), releases, addon.Channels, addon.DefaultPreference);
            }
        }
        catch (SessionExpiredException)
        {
            _logger.Info("CurseForge manifest check: 401, session expired");
            SignOutTo(GateFailure.SessionExpired);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or OperationCanceledException)
        {
            _logger.Warn(ex, "CurseForge manifest check failed");
            StatusMessage = $"CurseForge check failed: {ex.Message}";
        }

        return true;
    }

    private Task<bool> CheckProviderAddonsAsync(bool background)
    {
        var due = !background || DateTimeOffset.Now - _lastCurseForgeCheck >= CurseForgeCheckInterval;
        var addons = AllProviderAddons().Where(addon => due || !_releases.ContainsKey(addon.Id)).ToList();
        if (due)
        {
            _lastCurseForgeCheck = DateTimeOffset.Now;
        }

        return ProbeProviderAddonsAsync(addons);
    }
}
