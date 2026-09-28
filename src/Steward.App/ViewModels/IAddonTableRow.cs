namespace Steward.App.ViewModels;

public interface IAddonTableRow
{
    string DisplayName { get; }

    string Source { get; }

    string HiddenId { get; }

    bool IsHidden { get; set; }

    bool IsPendingUpdate { get; }

    int StatusRank { get; }

    DateTimeOffset? LastUpdated { get; }
}
