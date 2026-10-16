using CommunityToolkit.Mvvm.Input;

namespace Steward.App.ViewModels;

public sealed class BannerActionViewModel
{
    public required string Label { get; init; }

    public required IRelayCommand Command { get; init; }
}

public sealed class BannerViewModel
{
    public required string Id { get; init; }

    public required int Revision { get; init; }

    public required string Title { get; init; }

    public required string Message { get; init; }

    public bool HasMessage => Message.Length > 0;

    public required string Background { get; init; }

    public required string IconForeground { get; init; }

    public required string IconGlyph { get; init; }

    public required bool IsDismissible { get; init; }

    public required IReadOnlyList<BannerActionViewModel> Actions { get; init; }

    // Shared by the close button and any explicit "dismiss" action, since both record the same (id, revision) and hide the banner the same way.
    public required IRelayCommand DismissCommand { get; init; }
}
