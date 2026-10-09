using System.Text.Json;

using Steward.Core;
using Steward.Core.Diagnostics;

namespace Steward.App.ViewModels;

public sealed partial class MainViewModel
{
    private Task InstallRequirementsAsync(
        WowInstallViewModel install, ManagedAddon dependant, string channel, AddonRelease release, Action<string> status, IProgress<double> progress) =>
        AddonRequirements.InstallAsync(dependant, release, new AddonRequirementHooks(
            requirement => ResolveRequirement(install, requirement),
            (addon, fetched) => IsRequirementSatisfied(install, addon, fetched),
            (addon, cancellationToken) => FetchRequirementAsync(addon, channel, cancellationToken),
            (addon, requiredChannel, requiredRelease, cancellationToken) => InstallRequirementAsync(install, addon, requiredChannel, requiredRelease, progress, cancellationToken),
            status), CancellationToken.None);

    private ManagedAddon? ResolveRequirement(WowInstallViewModel install, AddonRequirement requirement)
    {
        var known = AddonsFor(install.FlavourPath);
        if ((known.FirstOrDefault(addon => AddonRequirements.Matches(addon.Id, requirement.Id))
            ?? known.FirstOrDefault(addon => requirement.FolderName is { Length: > 0 } folder && string.Equals(addon.FolderName, folder, StringComparison.OrdinalIgnoreCase))) is { } existing)
        {
            return existing;
        }

        if (AddonRequirements.CurseForgeModId(requirement.Id) is { } modId)
        {
            var versionType = CurseForgeVersionType(install.Install);
            var baseUrl = versionType is { } type ? _stewardClient.CurseForgeManifestBaseUrl(modId, type) : requirement.ManifestBaseUrl;
            return baseUrl is null
                ? null
                : new ManagedAddon(
                    versionType is { } recorded ? CurseForgeAddons.Id(modId, recorded) : requirement.Id,
                    requirement.FolderName ?? "",
                    baseUrl,
                    Name: requirement.Name,
                    Features: [StewardClient.AddonsFeature],
                    Source: CurseForgeAddons.Source);
        }

        return requirement is { FolderName.Length: > 0, ManifestBaseUrl.Length: > 0 }
            ? new ManagedAddon(requirement.Id, requirement.FolderName, requirement.ManifestBaseUrl, Name: requirement.Name)
            : null;
    }

    private bool IsRequirementSatisfied(WowInstallViewModel install, ManagedAddon addon, AddonRelease? release)
    {
        if (_stateStore.Load().IgnoredAddons.Contains(AppStateStore.Key(install.FlavourPath, addon.Id), StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        var folder = addon.FolderName.Length > 0 ? addon.FolderName
            : release?.Folders is { Count: > 0 } folders ? CurseForgeAddons.PrimaryFolder(folders)
            : null;
        var path = folder is null ? null : Path.Combine(install.AddOnsPath, folder);
        return path is not null && Directory.Exists(path) && LocalAddons.TopLevelToc(path, folder!) is not null;
    }

    private async Task<(string Channel, AddonRelease Release)?> FetchRequirementAsync(ManagedAddon addon, string channel, CancellationToken cancellationToken)
    {
        var releases = await _addonUpdater.ProbeChannelsAsync(addon, VisibleChannels, cancellationToken).ConfigureAwait(true);
        var resolved = AddonChannelStatus.Resolve(channel, releases, addon.Channels, addon.DefaultPreference);
        _releases[addon.Id] = releases;
        _status[addon.Id] = resolved;
        return resolved is { Channel: { } resolvedChannel, Release: { } resolvedRelease } ? (resolvedChannel, resolvedRelease) : null;
    }

    private async Task InstallRequirementAsync(
        WowInstallViewModel install, ManagedAddon addon, string channel, AddonRelease release, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var folders = await _addonUpdater.InstallAsync(addon, channel, release, install.AddOnsPath, progress, cancellationToken).ConfigureAwait(true);
        var state = _stateStore.Load();
        state.Installs[AppStateStore.Key(install.FlavourPath, addon.Id)] = new InstalledAddonRecord(release.Version, channel, release.Sha256, DateTimeOffset.Now, release.Sha1, folders);
        var records = state.ProviderAddons.GetValueOrDefault(install.FlavourPath) ?? [];
        if (CurseForgeEnabled
            && addon.Id.Split('-') is ["curseforge", var mod, var type]
            && int.TryParse(mod, out var modId)
            && int.TryParse(type, out var versionType)
            && !records.Any(record => string.Equals(record.Id, addon.Id, StringComparison.OrdinalIgnoreCase)))
        {
            IReadOnlyList<string> modules = release.Folders is { Count: > 0 } listed ? listed : folders;
            var primary = CurseForgeAddons.PrimaryFolder(modules);
            string? iconUrl = null;
            try
            {
                iconUrl = await _stewardClient.GetCurseForgeIconUrlAsync(modId, versionType, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is HttpRequestException or StewardRequestException or TaskCanceledException or JsonException)
            {
                _logger.Warn(ex, $"CurseForge icon for {addon.Id} failed");
            }

            records = [.. records, new ProviderAddonRecord(addon.Id, primary, addon.Name ?? primary, CurseForgeAddons.Source, modId, versionType, modules, iconUrl, release.Website)];
            SaveProviderRecords(state, install.FlavourPath, records);
        }
        else
        {
            _stateStore.Save(state);
        }

        _logger.Info($"Installed {addon.Id} {release.Version} on {install.FlavourPath} as a requirement");
        SyncProviderRows(install);
        install.AddonRows.FirstOrDefault(row => string.Equals(row.AddonId, addon.Id, StringComparison.OrdinalIgnoreCase))?.RefreshInstalledVersion();
    }
}
