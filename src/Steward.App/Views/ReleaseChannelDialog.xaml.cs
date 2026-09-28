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
            Options.Items.Add(new RadioButton { Content = OptionContent(option), IsEnabled = option.IsEnabled, Tag = option.Channel });
        }

        Options.SelectedIndex = channel.Options.ToList().FindIndex(option => option.IsCurrent);
    }

    public AddonChannelViewModel Channel { get; }

    private string? SelectedChannel => (Options.SelectedItem as RadioButton)?.Tag as string;

    private static StackPanel OptionContent(ChannelOption option)
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

        var detail = new TextBlock { Text = option.Detail, FontSize = 11.5, Foreground = Brush(option.IsEnabled ? "TextFillColorSecondaryBrush" : "TextFillColorDisabledBrush") };
        if (option.IsEnabled)
        {
            detail.FontFamily = new FontFamily("Cascadia Mono");
        }

        var content = new StackPanel { Spacing = 2 };
        content.Children.Add(title);
        content.Children.Add(detail);
        return content;
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
