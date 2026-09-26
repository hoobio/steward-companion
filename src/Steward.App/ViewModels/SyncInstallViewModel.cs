using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml;

namespace Steward.App.ViewModels;

public sealed partial class SyncInstallViewModel : ObservableObject
{
    public required string DisplayName { get; init; }

    public required string FlavourPath { get; init; }

    [ObservableProperty]
    public partial string? ClientVersion { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunningPillVisibility))]
    public partial bool IsClientRunning { get; set; }

    [ObservableProperty]
    public partial bool AddonMissing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReadErrorVisibility))]
    public partial string? ReadError { get; set; }

    public ObservableCollection<SyncDatasetViewModel> Datasets { get; } = [];

    public Visibility RunningPillVisibility => When(IsClientRunning);

    public Visibility ReadErrorVisibility => When(ReadError is not null);

    public void CopyFrom(SyncInstallViewModel fresh)
    {
        ClientVersion = fresh.ClientVersion;
        IsClientRunning = fresh.IsClientRunning;
        AddonMissing = fresh.AddonMissing;
        ReadError = fresh.ReadError;
        for (var i = 0; i < Datasets.Count; i++)
        {
            Datasets[i].CopyFrom(fresh.Datasets[i]);
        }
    }

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;
}
