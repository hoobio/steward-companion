using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.Core;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Steward.App.ViewModels;

public sealed partial class WowInstallViewModel : ObservableObject
{
    private static readonly string[] RowTriggers =
    [
        nameof(AddonRowViewModel.IsBusy),
        nameof(AddonRowViewModel.InstalledVersion),
        nameof(AddonRowViewModel.AvailableVersion),
        nameof(AddonRowViewModel.Channel),
        nameof(AddonRowViewModel.HasFailed),
    ];

    private readonly Action<WowInstallViewModel> _remove;

    public WowInstallViewModel(
        WowInstall install,
        IReadOnlyList<ManagedAddon> addons,
        AddonUpdater updater,
        AppStateStore stateStore,
        Func<CancellationToken, Task<bool>> ensureAuthorized,
        Action changeChannelRequested,
        Action<WowInstallViewModel> remove)
    {
        ArgumentNullException.ThrowIfNull(addons);

        Install = install;
        _remove = remove;
        foreach (var addon in addons)
        {
            var row = new AddonRowViewModel(install, addon, updater, stateStore, ensureAuthorized, changeChannelRequested)
            {
                IsFirst = AddonRows.Count == 0,
            };
            row.PropertyChanged += OnRowPropertyChanged;
            AddonRows.Add(row);
        }
    }

    public event EventHandler? RowsChanged;

    public WowInstall Install { get; }

    public string Flavour => Install.Flavour;

    public string DisplayName => WowInstalls.DisplayName(Install.Flavour);

    public string FlavourPath => Install.FlavourPath;

    public string AddOnsPath => Install.AddOnsPath;

    public string? ClientVersion => Install.ClientVersion;

    public ObservableCollection<AddonRowViewModel> AddonRows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddedByYouVisibility))]
    public partial bool IsAddedByUser { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunningDotVisibility))]
    public partial bool IsClientRunning { get; set; }

    public Visibility RunningDotVisibility => IsClientRunning ? Visibility.Visible : Visibility.Collapsed;

    public int UpdateCount => AddonRows.Count(row => row.HasUpdateAvailable);

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
        IsClientRunning = WowClient.IsRunning(Install);
        foreach (var row in AddonRows)
        {
            row.IsClientRunning = IsClientRunning;
            if (!IsClientRunning)
            {
                row.NeedsReload = false;
            }
        }
    }

    public void SetIsAdmin(bool isAdmin)
    {
        foreach (var row in AddonRows)
        {
            row.IsAdmin = isAdmin;
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
