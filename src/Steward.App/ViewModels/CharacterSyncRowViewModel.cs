using Microsoft.UI.Xaml;

namespace Steward.App.ViewModels;

public sealed record CharacterSyncRowViewModel(
    string DisplayName,
    string FlavourPath,
    DateTimeOffset? PushedAt,
    string? Error)
{
    public string LastPushText => PushedAt is null ? "Not pushed yet" : $"Pushed {Relative(PushedAt.Value)}";

    public Visibility ErrorVisibility => Error is not null ? Visibility.Visible : Visibility.Collapsed;

    private static string Relative(DateTimeOffset moment)
    {
        var elapsed = DateTimeOffset.Now - moment;
        return true switch
        {
            _ when elapsed < TimeSpan.FromMinutes(1) => "just now",
            _ when elapsed < TimeSpan.FromHours(1) =>
                $"{(int)elapsed.TotalMinutes} minute{((int)elapsed.TotalMinutes == 1 ? "" : "s")} ago",
            _ when elapsed < TimeSpan.FromDays(1) =>
                $"{(int)elapsed.TotalHours} hour{((int)elapsed.TotalHours == 1 ? "" : "s")} ago",
            _ => $"{(int)elapsed.TotalDays} day{((int)elapsed.TotalDays == 1 ? "" : "s")} ago",
        };
    }
}
