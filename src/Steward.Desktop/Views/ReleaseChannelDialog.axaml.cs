using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;

using FluentAvalonia.UI.Controls;

using Steward.App.ViewModels;

namespace Steward.Desktop.Views;

internal sealed record ChannelRow(ChannelOption Option)
{
    public bool HasChangelog => Option.IsEnabled && Option.Changelog.Count > 0;

    public string ChangelogName => $"Changelog for {Option.Release?.Version}";
}

public sealed partial class ReleaseChannelDialog : FAContentDialog
{
    private string? _selectedChannel;
    private string? _shownChangelog;

    public ReleaseChannelDialog(AddonChannelViewModel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        Channel = channel;
        DataContext = channel;
        InitializeComponent();
        Options.ItemsSource = channel.Options.Select(option => new ChannelRow(option)).ToList();
        _selectedChannel = channel.Options.FirstOrDefault(option => option.IsCurrent)?.Channel;
    }

    public AddonChannelViewModel Channel { get; }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    private void OnOptionLoaded(object? sender, RoutedEventArgs e)
    {
        var radio = (RadioButton)sender!;
        radio.IsChecked = (string?)radio.Tag == _selectedChannel;
        // The RadioButton template pins the glyph grid to VerticalAlignment Top, so a two-line option has its circle beside the first line only.
        if (radio.GetVisualDescendants().OfType<Ellipse>().FirstOrDefault(ellipse => ellipse.Name == "OuterEllipse")?.GetVisualParent() is Layoutable glyph)
        {
            glyph.VerticalAlignment = VerticalAlignment.Center;
        }
    }

    private void OnOptionChecked(object? sender, RoutedEventArgs e)
    {
        var radio = (RadioButton)sender!;
        if (radio.IsChecked != true)
        {
            return;
        }

        _selectedChannel = (string?)radio.Tag;
        PrimaryButtonText = _selectedChannel is { } selected && !string.Equals(selected, Channel.Current, StringComparison.OrdinalIgnoreCase)
            ? $"Use {selected}"
            : "Save";
    }

    private void OnChangelogClick(object? sender, RoutedEventArgs e)
    {
        var option = ((ChannelRow)((Control)sender!).DataContext!).Option;
        e.Handled = true;
        if (ChangelogPanel.IsVisible && _shownChangelog == option.Channel)
        {
            ChangelogPanel.IsVisible = false;
            _shownChangelog = null;
            return;
        }

        _shownChangelog = option.Channel;
        ChangelogCaption.Text = $"{option.Label} {option.Release!.Version} changelog";
        ChangelogHost.Blocks = option.Changelog;
        ChangelogPanel.IsVisible = true;
    }

    private void OnSaveClick(FAContentDialog sender, FAContentDialogButtonClickEventArgs args)
    {
        if (_selectedChannel is { } channel)
        {
            Channel.Save(channel);
        }
    }
}
