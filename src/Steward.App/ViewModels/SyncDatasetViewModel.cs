using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Steward.App.ViewModels;

public sealed partial class SyncDatasetViewModel : ObservableObject
{
    public required string Key { get; init; }

    public required string Name { get; init; }

    public required string Unit { get; set; }

    public required string SourceFile { get; set; }

    public required int LocalCount { get; set; }

    public required string ExportedAtText { get; set; }

    public required bool IsStale { get; set; }

    public required bool IsFirst { get; init; }

    public bool IsSynced { get; set; }

    public Visibility SyncedGlyphVisibility => When(IsSynced);

    public ImageSource? IconSource { get; set; }

    public string? StatusText { get; set; }

    [ObservableProperty]
    public partial string? Note { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteVisibility))]
    public partial CharacterSyncRowViewModel? CharacterSync { get; set; }

    public Visibility NoteVisibility => When(CharacterSync is null && Note is not null);

    public Thickness HairlineThickness => IsFirst ? default : new Thickness(0, 1, 0, 0);

    public string RecordsText => $"{LocalCount} {Unit}";

    public void CopyFrom(SyncDatasetViewModel fresh)
    {
        SourceFile = fresh.SourceFile;
        LocalCount = fresh.LocalCount;
        Unit = fresh.Unit;
        ExportedAtText = fresh.ExportedAtText;
        IsStale = fresh.IsStale;
        IsSynced = fresh.IsSynced;
        IconSource = fresh.IconSource;
        StatusText = fresh.StatusText;
        Note = fresh.Note;
        OnPropertyChanged(string.Empty);
    }

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    partial void OnNoteChanged(string? value) => OnPropertyChanged(nameof(NoteVisibility));
}
