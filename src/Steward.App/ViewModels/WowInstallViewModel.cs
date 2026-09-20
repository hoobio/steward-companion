using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.Core;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Steward.App.ViewModels;

public sealed partial class WowInstallViewModel : ObservableObject, IDisposable
{
    private static readonly string[] RowTriggers =
    [
        nameof(AddonRowViewModel.IsBusy),
        nameof(AddonRowViewModel.InstalledVersion),
        nameof(AddonRowViewModel.AvailableVersion),
        nameof(AddonRowViewModel.Channel),
        nameof(AddonRowViewModel.HasFailed),
        nameof(AddonRowViewModel.IsHidden),
    ];

    private readonly Action<WowInstallViewModel> _remove;
    private readonly Action<WowInstallViewModel> _clientExited;

    private CancellationTokenSource? _watchCts;

    public WowInstallViewModel(
        WowInstall install,
        IReadOnlyList<ManagedAddon> addons,
        AddonUpdater updater,
        AppStateStore stateStore,
        Func<CancellationToken, Task<bool>> ensureAuthorized,
        Action changeChannelRequested,
        Action<WowInstallViewModel> remove,
        Action<WowInstallViewModel> clientExited,
        Func<WowInstall, Task> afterStewardInstalled)
    {
        ArgumentNullException.ThrowIfNull(addons);

        Install = install;
        _remove = remove;
        _clientExited = clientExited;
        foreach (var addon in addons)
        {
            var row = new AddonRowViewModel(
                install,
                addon,
                updater,
                stateStore,
                ensureAuthorized,
                changeChannelRequested,
                afterStewardInstalled)
            {
                IsFirst = AddonRows.Count == 0,
            };
            row.PropertyChanged += OnRowPropertyChanged;
            AddonRows.Add(row);
        }
    }

    public event EventHandler? RowsChanged;

    public WowInstall Install { get; }

    public string DisplayName => Install.DisplayName;

    public string FlavourPath => Install.FlavourPath;

    public string AddOnsPath => Install.AddOnsPath;

    public string? ClientVersion => Install.ClientVersion;

    public ObservableCollection<AddonRowViewModel> AddonRows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddedByYouVisibility))]
    public partial bool IsAddedByUser { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClientRunning), nameof(RunningDotVisibility))]
    public partial WowClientProcess? Client { get; set; }

    public bool IsClientRunning => Client is not null;

    public Visibility RunningDotVisibility => IsClientRunning ? Visibility.Visible : Visibility.Collapsed;

    public int UpdateCount => AddonRows.Count(row => !row.IsHidden && row.HasUpdateAvailable);

    public string CountPillText => UpdateCount switch
    {
        0 => "Up to date",
        1 => "1 update",
        var n => $"{n} updates",
    };

    public Brush CountPillBrush => (Brush)Application.Current.Resources[
        UpdateCount > 0 ? "CautionTintBrush" : "SuccessTintBrush"];

    public Visibility AddedByYouVisibility => IsAddedByUser ? Visibility.Visible : Visibility.Collapsed;

    public void ApplyStatus(IReadOnlyDictionary<string, AddonChannelStatus> status, bool background)
    {
        ArgumentNullException.ThrowIfNull(status);

        foreach (var row in AddonRows)
        {
            if (background && row.IsBusy) { continue; }
            if (status.TryGetValue(row.AddonId, out var addonStatus)) { row.Apply(addonStatus); }
        }

        Recompute();
    }

    public void RefreshClientRunning()
    {
        var client = WowClient.Find(Install);
        if (client?.ProcessId != Client?.ProcessId)
        {
            StopWatching();
            if (client is not null)
            {
                _watchCts = new CancellationTokenSource();
                _ = WatchExitAsync(client.ProcessId, _watchCts.Token);
            }
        }

        Client = client;
        foreach (var row in AddonRows)
        {
            row.IsClientRunning = IsClientRunning;
            if (!IsClientRunning)
            {
                row.NeedsReload = false;
            }
        }
    }

    public void Dispose() => StopWatching();

    public void SetIsAdmin(bool isAdmin)
    {
        foreach (var row in AddonRows)
        {
            row.IsAdmin = isAdmin;
        }
    }

    public void SetShowHidden(bool showHidden)
    {
        foreach (var row in AddonRows)
        {
            row.ShowHidden = showHidden;
        }
    }

    public void SyncHidden(IReadOnlySet<string> hiddenAddonIds)
    {
        foreach (var row in AddonRows)
        {
            row.IsHidden = hiddenAddonIds.Contains(row.AddonId);
        }
    }

    public void Recompute()
    {
        OnPropertyChanged(nameof(UpdateCount));
        OnPropertyChanged(nameof(CountPillText));
        OnPropertyChanged(nameof(CountPillBrush));
    }

    [RelayCommand]
    private void Remove() => _remove(this);

    [RelayCommand]
    private void OpenFolder() =>
        Process.Start(new ProcessStartInfo(AddOnsPath) { UseShellExecute = true })?.Dispose();

    private void StopWatching()
    {
        _watchCts?.Cancel();
        _watchCts?.Dispose();
        _watchCts = null;
    }

    private async Task WatchExitAsync(int processId, CancellationToken cancellationToken)
    {
        try
        {
            await WowClient.WaitForExitAsync(processId, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        RefreshClientRunning();
        _clientExited(this);
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null || !RowTriggers.Contains(e.PropertyName))
        {
            return;
        }

        Recompute();
        RowsChanged?.Invoke(this, EventArgs.Empty);
    }
}
