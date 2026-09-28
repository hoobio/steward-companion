using Microsoft.UI.Xaml;

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

    public Visibility SkillsVisibility => When(SkillsSummary.Length > 0);

    public Visibility SyncedVisibility => When(State == ProfessionsCharacterState.Synced);

    public Visibility PendingVisibility => When(State == ProfessionsCharacterState.Pending);

    public Visibility RejectedVisibility => When(State == ProfessionsCharacterState.Rejected);

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;
}
