using CommunityToolkit.Mvvm.ComponentModel;

using Steward.Core;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Steward.App.ViewModels;

public sealed partial class GuildOptionViewModel : ObservableObject
{
    public GuildOptionViewModel(AdminGuild guild, string role)
    {
        ArgumentNullException.ThrowIfNull(guild);

        Id = guild.Id;
        Label = guild.Name is { Length: > 0 } name ? name : guild.Id;
        MemberCount = guild.MemberCount;
        Role = role;
        Initial = Label[..1].ToUpperInvariant();
        Icon = Uri.TryCreate(guild.IconUrl, UriKind.Absolute, out var icon) ? new BitmapImage(icon) : null;
    }

    public string Id { get; }

    public string Label { get; }

    public int MemberCount { get; }

    public string Role { get; }

    public string Detail => $"{MemberCount} member{(MemberCount == 1 ? "" : "s")} · {Role}";

    public string Initial { get; }

    public ImageSource? Icon { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentVisibility))]
    public partial bool IsCurrent { get; set; }

    public Visibility CurrentVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;
}
