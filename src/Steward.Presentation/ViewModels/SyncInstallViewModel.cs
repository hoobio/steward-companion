using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Steward.Core;

namespace Steward.App.ViewModels;

public sealed partial class SyncInstallViewModel : ObservableObject
{
    public required string DisplayName { get; init; }

    public required string FlavourPath { get; init; }

    [ObservableProperty]
    public partial string? ClientVersion { get; set; }

    [ObservableProperty]
    public partial bool IsClientRunning { get; set; }

    [ObservableProperty]
    public partial bool AddonMissing { get; set; }

    [ObservableProperty]
    public partial int OutdatedProfessions { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoOwnCharactersVisibility))]
    public partial bool HasNoOwnCharacters { get; set; }

    public bool NoOwnCharactersVisibility => HasNoOwnCharacters;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OldFormatVisibility))]
    [NotifyPropertyChangedFor(nameof(DatasetsVisibility))]
    public partial SyncExportState ExportState { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReadErrorVisibility))]
    [NotifyPropertyChangedFor(nameof(OldFormatVisibility))]
    [NotifyPropertyChangedFor(nameof(DatasetsVisibility))]
    public partial string? ReadError { get; set; }

    public ObservableCollection<SyncDatasetViewModel> Datasets { get; } = [];

    public ObservableCollection<SyncDatasetViewModel> PullDatasets { get; } = [];

    public ObservableCollection<ProfessionsCharacterViewModel> ProfessionsCharacters { get; } = [];

    public bool ShowsProfessionsHairline => PullDatasets.Count > 0;

    public string Shape => $"{FlavourPath}|{string.Join(',', Datasets.Select(dataset => dataset.Key))}";

    public bool ReadErrorVisibility => ReadError is not null;

    public bool OldFormatVisibility => ReadError is null && ExportState == SyncExportState.OldFormat;

    public bool DatasetsVisibility => ReadError is null && ExportState != SyncExportState.OldFormat;

    [ObservableProperty]
    public partial SyncDatasetViewModel? ProfessionsDataset { get; set; }

    [ObservableProperty]
    public partial bool IsProfessionsExpanded { get; set; }

    public Action<string, bool>? ExpansionChosen { get; set; }

    private bool _applyingExpansion;

    public void ApplyExpansion(bool expanded)
    {
        _applyingExpansion = true;
        IsProfessionsExpanded = expanded;
        _applyingExpansion = false;
    }

    partial void OnIsProfessionsExpandedChanged(bool value)
    {
        if (!_applyingExpansion)
        {
            ExpansionChosen?.Invoke(FlavourPath, value);
        }
    }

    public void CopyFrom(SyncInstallViewModel fresh)
    {
        ClientVersion = fresh.ClientVersion;
        IsClientRunning = fresh.IsClientRunning;
        AddonMissing = fresh.AddonMissing;
        OutdatedProfessions = fresh.OutdatedProfessions;
        HasNoOwnCharacters = fresh.HasNoOwnCharacters;
        ExportState = fresh.ExportState;
        ReadError = fresh.ReadError;
        ApplyExpansion(fresh.IsProfessionsExpanded);
        for (var i = 0; i < fresh.ProfessionsCharacters.Count; i++)
        {
            if (i == ProfessionsCharacters.Count)
            {
                ProfessionsCharacters.Add(fresh.ProfessionsCharacters[i]);
            }
            else if (ProfessionsCharacters[i] != fresh.ProfessionsCharacters[i])
            {
                ProfessionsCharacters[i] = fresh.ProfessionsCharacters[i];
            }
        }

        while (ProfessionsCharacters.Count > fresh.ProfessionsCharacters.Count)
        {
            ProfessionsCharacters.RemoveAt(ProfessionsCharacters.Count - 1);
        }

        for (var i = 0; i < Datasets.Count; i++)
        {
            Datasets[i].CopyFrom(fresh.Datasets[i]);
        }
    }
}
