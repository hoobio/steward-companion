using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Steward.App.ViewModels;

public sealed partial class SyncDatasetViewModel : ObservableObject
{
    private static readonly string[] CharacterSyncDerivedNames =
    [
        nameof(CharacterSyncVisibility),
        nameof(NoteVisibility),
    ];

    public required string Key { get; init; }

    public required string Name { get; init; }

    public required string Unit { get; init; }

    public required string Glyph { get; init; }

    public required string SourceFile { get; set; }

    public required int LocalCount { get; set; }

    public required string ExportedAtText { get; set; }

    public required bool IsStale { get; set; }

    public required bool IsFirst { get; init; }

    public bool IsComingSoon { get; init; }

    [ObservableProperty]
    public partial string? Note { get; set; }

    [ObservableProperty]
    public partial CharacterSyncRowViewModel? CharacterSync { get; set; }

    public Visibility CharacterSyncVisibility => When(CharacterSync is not null);

    public Visibility NoteVisibility => When(CharacterSync is null && Note is not null && !IsComingSoon);

    public Visibility ComingSoonVisibility => When(IsComingSoon);

    public Thickness HairlineThickness => IsFirst ? default : new Thickness(0, 1, 0, 0);

    public string RecordsText => $"{LocalCount} {Unit}";

    public Visibility RecordsVisibility => When(!IsComingSoon);

    public Visibility ExportedAtVisibility => When(!IsComingSoon);

    public Brush ExportedAtBrush => (Brush)Application.Current.Resources[
        IsStale ? "SystemFillColorCautionBrush" : "TextFillColorSecondaryBrush"];

    public void CopyFrom(SyncDatasetViewModel fresh)
    {
        SourceFile = fresh.SourceFile;
        LocalCount = fresh.LocalCount;
        ExportedAtText = fresh.ExportedAtText;
        IsStale = fresh.IsStale;
        Note = fresh.Note;
        OnPropertyChanged(string.Empty);
    }

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    partial void OnCharacterSyncChanged(CharacterSyncRowViewModel? value)
    {
        foreach (var name in CharacterSyncDerivedNames)
        {
            OnPropertyChanged(name);
        }
    }

    partial void OnNoteChanged(string? value) => OnPropertyChanged(nameof(NoteVisibility));
}
