using CommunityToolkit.Mvvm.ComponentModel;

using Steward.App.Services;
using Steward.Core;

namespace Steward.App.ViewModels;

public sealed partial class GuildOptionViewModel : ObservableObject
{
    public GuildOptionViewModel(AdminGuild guild, string role, IUiPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(guild);
        ArgumentNullException.ThrowIfNull(platform);

        Id = guild.Id;
        Label = guild.Name is { Length: > 0 } name ? name : guild.Id;
        MemberCount = guild.MemberCount;
        Role = role;
        Initial = Label[..1].ToUpperInvariant();
        Icon = Uri.TryCreate(guild.IconUrl, UriKind.Absolute, out var icon) ? platform.ImageFromUri(icon, 24) : null;
    }

    public string Id { get; }

    public string Label { get; }

    public int MemberCount { get; }

    public string Role { get; }

    public string Detail => $"{MemberCount} member{(MemberCount == 1 ? "" : "s")} · {Role}";

    public string Initial { get; }

    public object? Icon { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentVisibility))]
    public partial bool IsCurrent { get; set; }

    public bool CurrentVisibility => IsCurrent;
}
