using Steward.Core;

namespace Steward.App.ViewModels;

public sealed record ProfessionsCharacterViewModel(
    string Name,
    int Level,
    string ClassName,
    string SkillsSummary,
    ProfessionsCharacterState State,
    string? ReasonText)
{
    public string LevelClassText => $"Level {Level} {ClassName}";

    public bool SkillsVisibility => SkillsSummary.Length > 0;

    public bool SyncedVisibility => State == ProfessionsCharacterState.Synced;

    public bool PendingVisibility => State == ProfessionsCharacterState.Pending;

    public bool RejectedVisibility => State == ProfessionsCharacterState.Rejected;
}
