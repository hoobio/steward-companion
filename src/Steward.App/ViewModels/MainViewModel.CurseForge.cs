using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

using Steward.Core;
using Steward.App.Services;
using Steward.Core.Diagnostics;

using Microsoft.UI.Xaml;

namespace Steward.App.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly TimeSpan CurseForgeCheckInterval = TimeSpan.FromMinutes(30);

    private const string DefaultHandlerBannerId = "curseforge-default-handler";

    private DateTimeOffset _lastCurseForgeCheck;
    private int _defaultHandlerCheck;

    public async Task EvaluateCurseForgeDefaultHandlerAsync()
    {
        var check = ++_defaultHandlerCheck;
        await Task.Delay(500).ConfigureAwait(true);
        if (check != _defaultHandlerCheck)
        {
            return;
        }

        var aumid = CurseForgeDefaultQuery.Aumid;
        bool? isDefault = false;
        if (aumid is not null && IsAuthorized && HasCurseForgeFeature)
        {
            try
            {
                isDefault = await Task.Run(() => CurseForgeDefaultQuery.IsDefault(aumid)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not query the default app for curseforge links");
                return;
            }
        }

        var shown = _localBanners.Any(banner => banner.Id == DefaultHandlerBannerId);
        var wanted = aumid is not null && IsAuthorized && HasCurseForgeFeature && isDefault == false
            && !(_stateStore.Load().DismissedBanners ?? []).ContainsKey(DefaultHandlerBannerId);
        if (wanted && !shown)
        {
            ShowLocalInfoBanner(
                "Open CurseForge links with Steward",
                DefaultHandlerBannerId,
                "Set as default",
                () => _ = PickCurseForgeHandlerAsync(aumid!),
                () => _stateStore.Save(_stateStore.Load() with
                {
                    DismissedBanners = new Dictionary<string, int>(_stateStore.Load().DismissedBanners ?? [], StringComparer.Ordinal) { [DefaultHandlerBannerId] = 0 },
                }),
                "Install buttons on curseforge.com open Steward once it is the default app for CurseForge links.");
        }
        else if (!wanted && shown)
        {
            _localBanners.RemoveAll(banner => banner.Id == DefaultHandlerBannerId);
            RefreshBanners();
        }
    }

    private async Task PickCurseForgeHandlerAsync(string aumid)
    {
        try
        {
            // The picker offers "Always" only while no app is the default; otherwise it can only open the link once.
            if (CurseForgeDefaultQuery.HasAnyDefault())
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri($"ms-settings:defaultapps?registeredAUMID={Uri.EscapeDataString(aumid)}"));
                return;
            }

            var options = new Windows.System.LauncherOptions { DisplayApplicationPicker = true };
            WinRT.Interop.InitializeWithWindow.Initialize(options, OwnerWindowHandle);
            await Windows.System.Launcher.LaunchUriAsync(new Uri("curseforge://"), options);
            return;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "CurseForge app picker failed, opening Default apps instead");
        }

        await Windows.System.Launcher.LaunchUriAsync(new Uri($"ms-settings:defaultapps?registeredAUMID={Uri.EscapeDataString(aumid)}"));
    }

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

    private IReadOnlyList<string> ExcludedFolders(WowInstallViewModel install) => ExcludedFoldersForOthers(install, "");

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

    public Visibility GetAddonsVisibility =>
        HasCurseForgeFeature && SelectedInstall is { IsMissing: false } install && CurseForgeVersionType(install.Install) is not null ? Visibility.Visible : Visibility.Collapsed;

    public GetAddonsViewModel? CreateGetAddons() =>
        SelectedInstall is { } install && CurseForgeVersionType(install.Install) is { } versionType
            ? new GetAddonsViewModel(this, install, versionType, _gigagrugClient, _logger)
            : null;

    public static bool IsCurseForgeInstalled(WowInstallViewModel install, int modId, int versionType) =>
        install.AddonRows.Any(row => string.Equals(row.AddonId, CurseForgeAddons.Id(modId, versionType), StringComparison.OrdinalIgnoreCase) && row.IsInstalled);

    public Func<string, string, string?, string, Task<bool>>? ShowLinkDialog { get; set; }

    public Func<string, IReadOnlyList<string>, int, string, Task<int?>>? ChooseLinkFile { get; set; }

    public async Task ReceiveCurseForgeLinkAsync(string uri)
    {
        if (CurseForgeLinks.Parse(uri) is not { } link)
        {
            _logger.Info($"Ignored a CurseForge link that is not curseforge://install with two positive ids: {uri}");
            return;
        }

        _logger.Info($"CurseForge link received: mod {link.ModId}, file {link.FileId}");
        if (InitializeCommand.ExecutionTask is { } initializing)
        {
            await initializing.ConfigureAwait(true);
        }

        if (!IsSignedIn || !HasCurseForgeFeature)
        {
            ShowLocalInfoBanner("CurseForge installs are not enabled for your account.");
            return;
        }

        try
        {
            await HandleCurseForgeLinkAsync(link).ConfigureAwait(true);
        }
        catch (SessionExpiredException)
        {
            SignOutTo(GateFailure.SessionExpired);
        }
        catch (Exception ex) when (ex is HttpRequestException or GigagrugRequestException or TaskCanceledException or JsonException or COMException)
        {
            _logger.Warn(ex, $"CurseForge link for mod {link.ModId}, file {link.FileId} failed");
            StatusMessage = $"Could not open the CurseForge link: {ex.Message}";
        }
    }

    private async Task HandleCurseForgeLinkAsync(CurseForgeLink link)
    {
        if (await _gigagrugClient.GetCurseForgeFileAsync(link.ModId, link.FileId, CancellationToken.None).ConfigureAwait(true) is not { } file)
        {
            StatusMessage = "That CurseForge file was not found.";
            return;
        }

        if (SelectedInstall is not { } install || ShowLinkDialog is not { } show)
        {
            StatusMessage = "Pick a World of Warcraft install to add CurseForge addons to.";
            return;
        }

        var versionType = CurseForgeVersionType(install.Install);
        var id = versionType is { } type ? CurseForgeAddons.Id(file.ModId, type) : null;
        var row = install.AddonRows.FirstOrDefault(row => row.AddonId == id && row.IsInstalled);
        var installedAt = id is null ? null : _stateStore.Load().Installs.GetValueOrDefault(AppStateStore.Key(install.FlavourPath, id))?.InstalledAt;
        var installedReleased = id is null ? null : _releases.GetValueOrDefault(id)?.Values.FirstOrDefault(release => release?.Version == row?.InstalledVersion)?.Released;
        var newer = row is not null && CurseForgeLinks.IsNewer(file.File, row.InstalledVersion, installedReleased, installedAt);
        var state = CurseForgeLinks.State(file, versionType, row is not null, newer);
        var name = file.Name;
        var version = file.File.Version;
        var game = install.GameVersionName ?? install.Label;
        var channel = CurseForgeLinks.Channel(file.ReleaseType);
        _logger.Info($"CurseForge link for {name} {version} on {install.FlavourPath}: {state}");

        switch (state)
        {
            case CurseForgeLinkState.NotBuilt when versionType is null:
                await show($"{name} {version} is not built for {game}.", $"Steward cannot install CurseForge addons on {game}.", null, "Close").ConfigureAwait(true);
                return;
            case CurseForgeLinkState.NotBuilt:
                await InstallForThisClientAsync(install, link, file, versionType.Value, game, row).ConfigureAwait(true);
                return;
            case CurseForgeLinkState.Installed:
                NavigateToAddons?.Invoke();
                ShowLocalInfoBanner($"{name} {row!.InstalledVersion} is already installed.");
                return;
            case CurseForgeLinkState.NotDistributable:
                if (await show($"{name} is only available on CurseForge", $"Its author does not allow other apps to download it, so {version} has to be installed from CurseForge.", "Open on CurseForge", "Cancel").ConfigureAwait(true)
                    && Uri.TryCreate(file.File.Website ?? file.WebsiteUrl, UriKind.Absolute, out var website) && website.Scheme == Uri.UriSchemeHttps)
                {
                    Process.Start(new ProcessStartInfo(website.AbsoluteUri) { UseShellExecute = true })?.Dispose();
                }

                return;
        }

        await InstallLinkedAsync(install, file, versionType!.Value, channel, file.File).ConfigureAwait(true);
    }

    private async Task InstallForThisClientAsync(WowInstallViewModel install, CurseForgeLink link, CurseForgeModFile file, int versionType, string game, AddonRowViewModel? row)
    {
        var record = new ProviderAddonRecord(CurseForgeAddons.Id(file.ModId, versionType), file.Name, file.Name, CurseForgeAddons.Source, file.ModId, versionType, []);
        var probe = CurseForgeAddons.ToManagedAddon(record, _gigagrugClient.CurseForgeManifestBaseUrl(file.ModId, versionType));
        var releases = await _addonUpdater.ProbeChannelsAsync(probe, AddonChannelStatus.Ordered, CancellationToken.None).ConfigureAwait(true);
        if (CurseForgeLinks.Alternative(releases) is (var altChannel, var alt))
        {
            _logger.Info($"CurseForge link for {file.Name} {file.File.Version} is for another client; {game} has {alt.Version} on {altChannel}");
            if (row is not null && string.Equals(row.InstalledVersion, alt.Version, StringComparison.Ordinal))
            {
                NavigateToAddons?.Invoke();
                ShowLocalInfoBanner($"{file.Name} {alt.Version} is already installed.");
            }
            else if (await InstallLinkedAsync(install, file, versionType, altChannel, alt).ConfigureAwait(true))
            {
                ShowLocalInfoBanner($"Installed the {game} build {alt.Version} of {file.Name} instead of the linked file.");
            }

            return;
        }

        if (ChooseLinkFile is not { } choose)
        {
            return;
        }

        var choices = CurseForgeLinks.Choices(await _gigagrugClient.GetCurseForgeLatestFilesAsync(file.ModId, CancellationToken.None).ConfigureAwait(true), file, link.FileId);
        if (await choose(
                $"{file.Name} has no build for {game}.",
                [.. choices.Select(choice => $"{choice.Client} · {choice.Version}")],
                CurseForgeLinks.Preselect(choices, link.FileId),
                $"It will show as out of date and updates only once a {game} build is published.").ConfigureAwait(true) is not { } index)
        {
            return;
        }

        var chosen = choices[index].FileId == link.FileId
            ? file
            : await _gigagrugClient.GetCurseForgeFileAsync(file.ModId, choices[index].FileId, CancellationToken.None).ConfigureAwait(true);
        if (chosen is null)
        {
            StatusMessage = "That CurseForge file was not found.";
            return;
        }

        await InstallLinkedAsync(install, file, versionType, CurseForgeLinks.Channel(chosen.ReleaseType), chosen.File).ConfigureAwait(true);
    }

    private async Task<bool> InstallLinkedAsync(WowInstallViewModel install, CurseForgeModFile file, int versionType, string channel, AddonRelease release)
    {
        NavigateToAddons?.Invoke();
        var result = new CurseForgeResult(file.ModId, file.Name, null, null, file.IconUrl, file.WebsiteUrl, release.Version, file.AllowDistribution);
        if (await InstallCurseForgeAsync(install, result, versionType, (channel, release)).ConfigureAwait(true) is { } failure)
        {
            StatusMessage = failure;
            return false;
        }

        return true;
    }

    public Task<string?> InstallCurseForgeAsync(WowInstallViewModel install, CurseForgeResult result, int versionType) =>
        InstallCurseForgeAsync(install, result, versionType, null);

    private async Task<string?> InstallCurseForgeAsync(WowInstallViewModel install, CurseForgeResult result, int versionType, (string Channel, AddonRelease File)? pinned)
    {
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(result);
        if (!HasCurseForgeFeature)
        {
            return "CurseForge is not enabled for your account.";
        }

        var id = CurseForgeAddons.Id(result.Id, versionType);
        var draft = new ProviderAddonRecord(id, result.Name, result.Name, CurseForgeAddons.Source, result.Id, versionType, [], result.IconUrl, result.WebsiteUrl);
        var probe = CurseForgeAddons.ToManagedAddon(draft, _gigagrugClient.CurseForgeManifestBaseUrl(result.Id, versionType));
        IReadOnlyDictionary<string, AddonRelease?> releases;
        try
        {
            releases = await _addonUpdater.ProbeChannelsAsync(probe, VisibleChannels, CancellationToken.None).ConfigureAwait(true);
        }
        catch (SessionExpiredException)
        {
            SignOutTo(GateFailure.SessionExpired);
            return "Your session has expired.";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            _logger.Warn(ex, $"CurseForge manifest for {id} failed");
            return $"Could not reach CurseForge: {ex.Message}";
        }

        if (pinned is var (pinnedChannel, pinnedFile))
        {
            releases = new Dictionary<string, AddonRelease?>(releases) { [pinnedChannel] = pinnedFile };
        }

        var status = AddonChannelStatus.Resolve(pinned?.Channel ?? _stateStore.Load().Channels.GetValueOrDefault(id), releases, probe.Channels, probe.DefaultPreference);
        if (status.Release is not { Folders: { Count: > 0 } folders })
        {
            return $"No {install.GameVersionName} build of {result.Name} is on CurseForge.";
        }

        if (folders.FirstOrDefault(folder => ExcludedFoldersForOthers(install, id).Contains(folder, StringComparer.OrdinalIgnoreCase)) is { } taken)
        {
            return $"{taken} is already managed by another row.";
        }

        var state = _stateStore.Load();
        var records = (state.ProviderAddons.GetValueOrDefault(install.FlavourPath) ?? []).Where(record => record.Id != id).ToList();
        records.Add(draft with { FolderName = CurseForgeAddons.PrimaryFolder(folders), Folders = folders });
        if (pinned is { Channel: var channel })
        {
            state.Channels[id] = channel;
        }

        SaveProviderRecords(state, install.FlavourPath, records);
        _releases[id] = releases;
        _status[id] = status;
        SyncProviderRows(install);

        if (install.AddonRows.FirstOrDefault(row => row.AddonId == id) is not { } row || !row.UpdateCommand.CanExecute(null))
        {
            return "Could not start the install.";
        }

        await row.UpdateCommand.ExecuteAsync(null).ConfigureAwait(true);
        return row.HasFailed ? row.StatusMessage ?? "Install failed." : null;
    }

    private IReadOnlyList<string> ExcludedFoldersForOthers(WowInstallViewModel install, string id) =>
    [
        .. _addons.Select(addon => addon.FolderName),
        StewardGuidesAddon.FolderName,
        .. ProviderRecords(install.FlavourPath).Where(record => record.Id != id).SelectMany(CurseForgeAddons.Folders),
    ];

    private Task<bool> CheckProviderAddonsAsync(bool background)
    {
        var due = !background || IsDue(_lastCurseForgeCheck, CurseForgeCheckInterval);
        var addons = AllProviderAddons().Where(addon => due || !_releases.ContainsKey(addon.Id)).ToList();
        if (due)
        {
            _lastCurseForgeCheck = DateTimeOffset.Now;
        }

        return ProbeProviderAddonsAsync(addons);
    }
}
