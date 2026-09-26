using System.Windows.Input;

using Steward.Core;

using Microsoft.UI.Xaml;

namespace Steward.App.ViewModels;

public sealed record CharacterSyncRowViewModel(
    string DisplayName,
    string FlavourPath,
    DateTimeOffset? PushedAt,
    int? Accepted,
    string? Error,
    IReadOnlyList<CharacterSyncRejection> Rejected)
{
    public ICommand? SendNow { get; init; }

    public string LastPushText => PushedAt is null ? "Not pushed yet" : $"Pushed {Relative(PushedAt.Value)}";

    public string AcceptedText => Accepted is { } accepted ? $"{accepted} accepted" : string.Empty;

    public Visibility AcceptedVisibility => Accepted is not null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ErrorVisibility => Error is not null ? Visibility.Visible : Visibility.Collapsed;

    public bool HasRejected => Rejected.Count > 0;

    public Visibility RejectedVisibility => HasRejected ? Visibility.Visible : Visibility.Collapsed;

    public string RejectedSummary => string.Join(Environment.NewLine, Rejected.Select(r => $"{r.CharacterGuid}: {r.Reason}"));

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
