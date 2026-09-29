using Steward.App.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Steward.App.Views;

public sealed partial class ChooseGuildDialog : ContentDialog
{
    public ChooseGuildDialog(IReadOnlyList<GuildOptionViewModel> guilds, GuildOptionViewModel preselected)
    {
        ArgumentNullException.ThrowIfNull(guilds);

        InitializeComponent();
        foreach (var guild in guilds)
        {
            Options.Items.Add(new RadioButton { Content = OptionContent(guild), Tag = guild });
        }

        Options.SelectedIndex = guilds.ToList().IndexOf(preselected);
    }

    public GuildOptionViewModel? Selected => (Options.SelectedItem as RadioButton)?.Tag as GuildOptionViewModel;

    private static Grid OptionContent(GuildOptionViewModel guild)
    {
        var tile = new Grid();
        tile.Children.Add(new TextBlock
        {
            Text = guild.Initial,
            FontSize = 11.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("TextFillColorTertiaryBrush"),
        });
        tile.Children.Add(new Image { Source = guild.Icon, Stretch = Stretch.UniformToFill });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = guild.Label, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = guild.Detail, FontSize = 12, Foreground = Brush("TextFillColorSecondaryBrush") });

        var content = new Grid { ColumnSpacing = 12 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(6),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brush("ChipBrush"),
            Child = tile,
        });
        Grid.SetColumn(text, 1);
        content.Children.Add(text);
        return content;
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
