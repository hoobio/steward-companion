using Microsoft.UI.Xaml;

namespace Steward.App.ViewModels;

public sealed record ProfessionsCharacterViewModel(
    string Name,
    int Level,
    string ClassName,
    string SkillsSummary,
    bool? Accepted,
    string? ReasonText)
{
    public string LevelClassText => $"Level {Level} {ClassName}";

    public Visibility AcceptedVisibility => When(Accepted == true);

    public Visibility RejectedVisibility => When(Accepted == false);

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;
}

public sealed record UncapturedCharacterViewModel(string Name, int Level, string ClassName)
{
    public string LevelClassText => $"Level {Level} {ClassName}";
}
