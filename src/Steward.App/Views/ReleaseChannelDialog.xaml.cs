using CommunityToolkit.WinUI;

using Steward.App.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Steward.App.Views;

public sealed partial class ReleaseChannelDialog : ContentDialog
{
    public ReleaseChannelDialog(AddonChannelViewModel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        Channel = channel;
        InitializeComponent();
        foreach (var option in channel.Options)
        {
            var radio = new RadioButton { Content = OptionContent(option), IsEnabled = option.IsEnabled, Tag = option.Channel, Padding = new Thickness(8, 0, 0, 0) };
            radio.Loaded += CentreGlyph;
            Options.Items.Add(radio);
        }

        Options.SelectedIndex = channel.Options.ToList().FindIndex(option => option.IsCurrent);
    }

    public AddonChannelViewModel Channel { get; }

    private string? SelectedChannel => (Options.SelectedItem as RadioButton)?.Tag as string;

    private string? _shownChangelog;

    private StackPanel OptionContent(ChannelOption option)
    {
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock { Text = option.Label, FontSize = 14 });
        if (option.IsCurrent)
        {
            title.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 1, 7, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brush("SuccessTintBrush"),
                Child = new TextBlock { Text = "Current", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("SystemFillColorSuccessBrush") },
            });
        }

        var detail = new TextBlock { Text = option.Detail, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush(option.IsEnabled ? "TextFillColorSecondaryBrush" : "TextFillColorDisabledBrush") };
        if (option.IsEnabled)
        {
            detail.FontFamily = new FontFamily("Cascadia Mono");
        }

        var detailRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        detailRow.Children.Add(detail);
        if (option.IsEnabled && option.Changelog.Count > 0)
        {
            detailRow.Children.Add(ChangelogButton(option));
        }

        var content = new StackPanel { Spacing = 2 };
        content.Children.Add(title);
        content.Children.Add(detailRow);
        return content;
    }

    private Button ChangelogButton(ChannelOption option)
    {
        var button = new Button
        {
            Width = 22,
            Height = 22,
            MinHeight = 0,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon { Glyph = "", FontSize = 12, Foreground = Brush("TextFillColorSecondaryBrush") },
            Resources =
            {
                ["ButtonBackground"] = Brush("SubtleFillColorTransparentBrush"),
                ["ButtonBackgroundPointerOver"] = Brush("SubtleFillColorSecondaryBrush"),
                ["ButtonBackgroundPressed"] = Brush("SubtleFillColorTertiaryBrush"),
            },
        };
        var release = option.Release!;
        ToolTipService.SetToolTip(button, $"Changelog for {release.Version}");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Changelog for {release.Version}");
        button.Click += (_, _) => ToggleChangelog(option);
        return button;
    }

    private void ToggleChangelog(ChannelOption option)
    {
        if (ChangelogPanel.Visibility == Visibility.Visible && _shownChangelog == option.Channel)
        {
            ChangelogPanel.Visibility = Visibility.Collapsed;
            _shownChangelog = null;
            return;
        }

        _shownChangelog = option.Channel;
        ChangelogCaption.Text = $"{option.Label} {option.Release!.Version} changelog";
        ChangelogHost.Content = ChangelogView.Build(option.Changelog);
        ChangelogPanel.Visibility = Visibility.Visible;
    }

    // The RadioButton template pins the glyph grid to VerticalAlignment Top, so a two-line option has its circle beside the first line only.
    private static void CentreGlyph(object sender, RoutedEventArgs e)
    {
        if (((RadioButton)sender).FindDescendant("OuterEllipse") is { } ellipse && VisualTreeHelper.GetParent(ellipse) is FrameworkElement glyph)
        {
            glyph.VerticalAlignment = VerticalAlignment.Center;
        }
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        PrimaryButtonText = SelectedChannel is { } channel && !string.Equals(channel, Channel.Current, StringComparison.OrdinalIgnoreCase)
            ? $"Use {channel}"
            : "Save";

    private void OnSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (SelectedChannel is { } channel)
        {
            Channel.Save(channel);
        }
    }
}
