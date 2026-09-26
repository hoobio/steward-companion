using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.Core;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Steward.App.ViewModels;

public enum SyncDatasetState
{
    NothingYet,
    WaitingToSend,
    InSync,
    Sending,
    Failed,
    PullPending,
}

public sealed partial class SyncDatasetViewModel : ObservableObject
{
    private static readonly string[] DerivedNames =
    [
        nameof(State),
        nameof(NewCount),
        nameof(RecordsText),
        nameof(NewText),
        nameof(SendingText),
        nameof(RecordsVisibility),
        nameof(NewVisibility),
        nameof(NothingYetVisibility),
        nameof(SendingVisibility),
        nameof(InSyncVisibility),
        nameof(FailedVisibility),
        nameof(PullPendingVisibility),
        nameof(SendVisibility),
        nameof(ExportedAtVisibility),
    ];

    private static readonly string[] CharacterSyncDerivedNames =
    [
        nameof(CharacterSyncVisibility),
        nameof(PlaceholderVisibility),
        nameof(NewVisibility),
    ];

    public required string Key { get; init; }

    public required string Name { get; init; }

    public required string Unit { get; init; }

    public required string Glyph { get; init; }

    public required string SourceFile { get; set; }

    public required int LocalCount { get; set; }

    public required int ServerCount { get; set; }

    public required string ExportedAtText { get; set; }

    public required bool IsStale { get; set; }

    public required bool IsFirst { get; init; }

    public required SyncPayload Payload { get; set; }

    public required Func<SyncDatasetViewModel, Task> Send { get; init; }

    [ObservableProperty]
    public partial bool IsAdmin { get; set; }

    [ObservableProperty]
    public partial bool IsBlocked { get; set; }

    [ObservableProperty]
    public partial bool IsSending { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial CharacterSyncRowViewModel? CharacterSync { get; set; }

    public Visibility CharacterSyncVisibility => When(CharacterSync is not null);

    public Visibility PlaceholderVisibility => When(CharacterSync is null);

    public Thickness HairlineThickness => IsFirst ? default : new Thickness(0, 1, 0, 0);

    public int NewCount => Math.Max(0, LocalCount - ServerCount);

    public SyncDatasetState State => true switch
    {
        _ when IsSending => SyncDatasetState.Sending,
        _ when Error is not null => SyncDatasetState.Failed,
        _ when LocalCount == 0 && ServerCount == 0 => SyncDatasetState.NothingYet,
        _ when ServerCount > LocalCount => SyncDatasetState.PullPending,
        _ when NewCount > 0 => SyncDatasetState.WaitingToSend,
        _ => SyncDatasetState.InSync,
    };

    public string RecordsText => $"{LocalCount} records";

    public string NewText => $"{NewCount} new";

    public string SendingText => $"Sending {LocalCount} {Unit}";

    public string PullPendingText => $"{ServerCount} records on the server";

    public Visibility RecordsVisibility =>
        When(State is SyncDatasetState.WaitingToSend or SyncDatasetState.InSync or SyncDatasetState.Failed);

    public Visibility NewVisibility => When(CharacterSync is null && State == SyncDatasetState.WaitingToSend);

    public Visibility NothingYetVisibility => When(State == SyncDatasetState.NothingYet);

    public Visibility SendingVisibility => When(State == SyncDatasetState.Sending);

    public Visibility InSyncVisibility => When(State == SyncDatasetState.InSync);

    public Visibility FailedVisibility => When(State == SyncDatasetState.Failed);

    public Visibility PullPendingVisibility => When(State == SyncDatasetState.PullPending);

    public Visibility SendVisibility =>
        When(IsAdmin && !IsBlocked && State == SyncDatasetState.WaitingToSend);

    public Visibility ExportedAtVisibility => When(State != SyncDatasetState.NothingYet);

    public Brush ExportedAtBrush => (Brush)Application.Current.Resources[
        IsStale ? "SystemFillColorCautionBrush" : "TextFillColorSecondaryBrush"];

    public void CopyFrom(SyncDatasetViewModel fresh)
    {
        SourceFile = fresh.SourceFile;
        LocalCount = fresh.LocalCount;
        ServerCount = fresh.ServerCount;
        ExportedAtText = fresh.ExportedAtText;
        IsStale = fresh.IsStale;
        Payload = fresh.Payload;
        IsAdmin = fresh.IsAdmin;
        IsBlocked = fresh.IsBlocked;
        Error = fresh.Error;
        OnPropertyChanged(string.Empty);
    }

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    partial void OnIsAdminChanged(bool value) => NotifyDerived();

    partial void OnIsBlockedChanged(bool value) => NotifyDerived();

    partial void OnIsSendingChanged(bool value) => NotifyDerived();

    partial void OnErrorChanged(string? value) => NotifyDerived();

    partial void OnCharacterSyncChanged(CharacterSyncRowViewModel? value)
    {
        foreach (var name in CharacterSyncDerivedNames)
        {
            OnPropertyChanged(name);
        }
    }

    private void NotifyDerived()
    {
        foreach (var name in DerivedNames)
        {
            OnPropertyChanged(name);
        }
    }

    [RelayCommand]
    private Task SendDatasetAsync() => Send(this);
}
