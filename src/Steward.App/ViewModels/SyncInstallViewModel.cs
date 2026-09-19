using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml;

namespace Steward.App.ViewModels;

public sealed partial class SyncInstallViewModel : ObservableObject
{
    public required string DisplayName { get; init; }

    public required string FlavourPath { get; init; }

    public required string? ClientVersion { get; init; }

    public required bool IsClientRunning { get; init; }

    public required bool IsSample { get; init; }

    public required bool AddonMissing { get; init; }

    public required string? ReadError { get; init; }

    public ObservableCollection<SyncDatasetViewModel> Datasets { get; } = [];

    public Visibility RunningPillVisibility => When(IsClientRunning);

    public Visibility SamplePillVisibility => When(IsSample);

    public Visibility ReadErrorVisibility => When(ReadError is not null);

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;
}
