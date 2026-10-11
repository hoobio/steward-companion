using CommunityToolkit.Mvvm.ComponentModel;

using Steward.Core;
using Steward.Core.Diagnostics;

using Microsoft.UI.Xaml;

namespace Steward.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly HashSet<int> _lostAccessWaits = [];
    private readonly SessionRevocation _revocation = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessRemovedVisibility))]
    public partial string? AccessRemovedNotice { get; set; }

    public Visibility AccessRemovedVisibility => When(AccessRemovedNotice is not null);

    partial void OnIsSignedInChanged(bool value) => _revocation.SetSignedIn(value);

    public void ReportSessionRevoked()
    {
        if (!_revocation.Report())
        {
            return;
        }

        RunOnUi(() =>
        {
            if (IsSignedIn)
            {
                SignOutTo(GateFailure.SessionExpired);
            }
        });
    }

    private void QueueRevokedRemovals()
    {
        if (!_revocation.Take())
        {
            return;
        }

        var state = _stateStore.Load();
        var lost = AccessLoss.Revoke(state, _addonCatalogue, DateTimeOffset.Now);
        _logger.Info($"Session revoked: {lost.Values.Count(entry => entry.Due)} Steward addon install(s) queued for removal");
        _stateStore.Save(state with { LostAccess = lost });
    }

    private void TrackLostAccess(IReadOnlyList<ManagedAddon>? previous, IReadOnlyList<CatalogueAddon>? current)
    {
        var state = _stateStore.Load();
        var lost = AccessLoss.Track(state, previous, current, DateTimeOffset.Now);
        if (lost.Count != state.LostAccess.Count || lost.Any(entry => !state.LostAccess.TryGetValue(entry.Key, out var old) || old.Due != entry.Value.Due))
        {
            _stateStore.Save(state with { LostAccess = lost });
        }

        RemoveLostAccessAddons();
    }

    private void RemoveLostAccessAddons()
    {
        var state = _stateStore.Load();
        if (!state.LostAccess.Values.Any(entry => entry.Due))
        {
            return;
        }

        var clients = WowClient.Snapshot();
        var running = new List<WowClientProcess>();
        var removed = AccessLoss.RemoveDue(
            state,
            entry =>
            {
                if (WowClient.Find(entry.FlavourPath, clients) is { } client)
                {
                    running.Add(client);
                    return true;
                }

                return Installs.Any(install => string.Equals(install.FlavourPath, entry.FlavourPath, StringComparison.OrdinalIgnoreCase)
                    && install.AddonRows.Any(row => row.IsBusy && string.Equals(row.AddonId, entry.Id, StringComparison.OrdinalIgnoreCase)));
            },
            (entry, ex) => _logger.Warn(ex, $"Could not remove {entry.Id} from {entry.FlavourPath} after access ended"));
        _stateStore.Save(state);

        foreach (var entry in removed)
        {
            _logger.Info($"Removed {entry.Id} ({string.Join(", ", entry.Folders)}) from {entry.FlavourPath}: access ended");
            if (IsSignedIn)
            {
                ShowLocalInfoBanner(AccessLoss.Notice(entry));
            }
            else
            {
                AccessRemovedNotice = AccessRemovedNotice is { } earlier ? $"{earlier}\n{AccessLoss.Notice(entry)}" : AccessLoss.Notice(entry);
            }
        }

        foreach (var install in Installs.Where(install => removed.Any(entry => string.Equals(entry.FlavourPath, install.FlavourPath, StringComparison.OrdinalIgnoreCase))))
        {
            _ = install.RescanLocalAsync();
        }

        foreach (var client in running.Distinct())
        {
            if (_lostAccessWaits.Add(client.ProcessId))
            {
                _ = RemoveAfterExitAsync(client.ProcessId);
            }
        }
    }

    private async Task RemoveAfterExitAsync(int processId)
    {
        try
        {
            await WowClient.WaitForExitAsync(processId, CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            _lostAccessWaits.Remove(processId);
        }

        RemoveLostAccessAddons();
    }
}
