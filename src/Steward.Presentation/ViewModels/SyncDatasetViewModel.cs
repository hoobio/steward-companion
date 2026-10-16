using CommunityToolkit.Mvvm.ComponentModel;

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

    public bool SyncedGlyphVisibility => IsSynced;

    public object? IconSource { get; set; }

    public string? StatusText { get; set; }

    [ObservableProperty]
    public partial string? Note { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteVisibility))]
    public partial CharacterSyncRowViewModel? CharacterSync { get; set; }

    public bool NoteVisibility => CharacterSync is null && Note is not null;

    public bool ShowsHairline => !IsFirst;

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

    partial void OnNoteChanged(string? value) => OnPropertyChanged(nameof(NoteVisibility));
}
