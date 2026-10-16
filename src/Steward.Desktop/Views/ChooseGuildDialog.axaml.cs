using Avalonia.Controls;
using Avalonia.Interactivity;

using FluentAvalonia.UI.Controls;

using Steward.App.ViewModels;

namespace Steward.Desktop.Views;

public sealed partial class ChooseGuildDialog : FAContentDialog
{
    public ChooseGuildDialog(IReadOnlyList<GuildOptionViewModel> guilds, GuildOptionViewModel preselected)
    {
        ArgumentNullException.ThrowIfNull(guilds);

        Guilds = guilds;
        Selected = preselected;
        DataContext = this;
        InitializeComponent();
    }

    public IReadOnlyList<GuildOptionViewModel> Guilds { get; }

    public GuildOptionViewModel? Selected { get; private set; }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    private void OnGuildLoaded(object? sender, RoutedEventArgs e)
    {
        var radio = (RadioButton)sender!;
        radio.IsChecked = ReferenceEquals(radio.Tag, Selected);
    }

    private void OnGuildChecked(object? sender, RoutedEventArgs e)
    {
        var radio = (RadioButton)sender!;
        if (radio.IsChecked == true)
        {
            Selected = (GuildOptionViewModel)radio.Tag!;
        }
    }
}
