using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Steward.Core;

namespace Steward.App.ViewModels;

public sealed partial class WowInstallViewModel : ObservableObject
{
    public WowInstallViewModel(
        WowInstall install,
        IReadOnlyList<ManagedAddon> addons,
        AddonUpdater updater,
        AppStateStore stateStore,
        Func<CancellationToken, Task<bool>> ensureAuthorized)
    {
        Install = install;
        foreach (var addon in addons)
        {
            AddonRows.Add(new AddonRowViewModel(install, addon, updater, stateStore, ensureAuthorized));
        }
    }

    public WowInstall Install { get; }

    public string Flavour => Install.Flavour;

    public string? ClientVersion => Install.ClientVersion;

    public ObservableCollection<AddonRowViewModel> AddonRows { get; } = [];

    public void ApplyStatus(IReadOnlyDictionary<string, AddonChannelStatus> status, bool background)
    {
        foreach (var row in AddonRows)
        {
            if (background && row.IsBusy) { continue; }
            if (status.TryGetValue(row.AddonId, out var addonStatus)) { row.Apply(addonStatus); }
        }
    }

    public void SetIsAdmin(bool isAdmin)
    {
        foreach (var row in AddonRows)
        {
            row.IsAdmin = isAdmin;
        }
    }
}
