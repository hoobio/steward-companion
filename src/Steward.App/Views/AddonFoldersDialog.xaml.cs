using Steward.App.Services;
using Steward.App.ViewModels;
using Steward.Core;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Steward.App.Views;

public sealed partial class AddonFoldersDialog : ContentDialog
{
    public AddonFoldersDialog(string title, IReadOnlyList<FolderEntry> folders, ILogger logger)
    {
        InitializeComponent();
        Title = title;
        Caption.Text = $"Also installed with {title}:";
        foreach (var folder in folders)
        {
            _ = AddRowAsync(folder, logger);
        }
    }

    private async Task AddRowAsync(FolderEntry folder, ILogger logger)
    {
        var slot = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Rows.Children.Add(slot);
        var addon = await Task.Run(() => LocalAddons.Read(folder.AddOnsPath, folder.Folder, folder.ClientInterface)).ConfigureAwait(true);
        slot.Content = addon is null
            ? Row(new FontIcon { Glyph = "", FontSize = 15, Foreground = Brush("TextFillColorSecondaryBrush") }, null, folder, null)
            : Row(await Tile(folder, addon.Name, logger).ConfigureAwait(true), addon.Name, folder, addon);
    }

    public FolderEntry? ChangelogRequest { get; private set; }

    public void ClearChangelogRequest() => ChangelogRequest = null;

    private FrameworkElement VersionCell(FolderEntry folder, LocalAddon addon)
    {
        var shortVersion = VersionLabel.For(addon.Version, folder.Folder);
        var text = new TextBlock
        {
            Text = shortVersion,
            FontFamily = new FontFamily("Cascadia Mono"),
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("TextFillColorSecondaryBrush"),
        };
        var full = addon.Version is not null && addon.Version != shortVersion ? addon.Version : null;
        if (folder.Changelog is not { Count: > 0 })
        {
            if (full is not null)
            {
                ToolTipService.SetToolTip(text, full);
            }

            return text;
        }

        var content = new Grid { ColumnSpacing = 4 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(text);
        var icon = new FontIcon
        {
            Width = 12,
            Glyph = "",
            FontSize = 12,
            Margin = new Thickness(0, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("TextFillColorSecondaryBrush"),
        };
        Grid.SetColumn(icon, 1);
        content.Children.Add(icon);

        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            MinHeight = 0,
            Margin = new Thickness(-4, 0, 0, 0),
            Padding = new Thickness(4, 2, 4, 2),
            Content = content,
            Resources =
            {
                ["ButtonBackground"] = Brush("SubtleFillColorTransparentBrush"),
                ["ButtonBackgroundPointerOver"] = Brush("SubtleFillColorSecondaryBrush"),
                ["ButtonBackgroundPressed"] = Brush("SubtleFillColorTertiaryBrush"),
            },
        };
        ToolTipService.SetToolTip(button, string.Join("\n", new[] { full, "Changelog" }.OfType<string>()));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Changelog for {addon.Name}");
        button.Click += (_, _) =>
        {
            ChangelogRequest = folder;
            Hide();
        };
        return button;
    }

    private static async Task<UIElement> Tile(FolderEntry folder, string name, ILogger logger)
    {
        var tile = new Grid();
        tile.Children.Add(new TextBlock
        {
            Text = InitialsTile.Text(name),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = InitialsTile.Brush(folder.Folder),
        });
        if (await LocalAddonIcon.LoadAsync(folder.AddOnsPath, folder.Folder, logger).ConfigureAwait(true) is { } icon)
        {
            tile.Children.Add(new Image { Source = icon, Stretch = Stretch.UniformToFill });
        }

        return tile;
    }

    private Grid Row(UIElement icon, string? name, FolderEntry folder, LocalAddon? addon)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });

        row.Children.Add(new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(6),
            VerticalAlignment = VerticalAlignment.Center,
            Background = name is null ? null : Brush("ChipBrush"),
            Child = icon,
        });

        var lines = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        if (name is not null)
        {
            var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            title.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 13.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (!string.IsNullOrEmpty(folder.Notice?.Text))
            {
                title.Children.Add(NoticeButton(folder));
            }

            lines.Children.Add(title);
        }

        lines.Children.Add(new TextBlock
        {
            Text = folder.Folder,
            FontFamily = new FontFamily("Cascadia Mono"),
            FontSize = name is null ? 12 : 11,
            IsTextSelectionEnabled = true,
            Foreground = Brush(name is null ? "TextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush"),
        });
        Grid.SetColumn(lines, 1);
        row.Children.Add(lines);

        if (addon is not null)
        {
            var version = VersionCell(folder, addon);
            version.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(version, 2);
            row.Children.Add(version);
        }

        if (folder.Channel is not null)
        {
            var channel = new TextBlock
            {
                Text = folder.Channel,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush("TextFillColorSecondaryBrush"),
            };
            Grid.SetColumn(channel, 3);
            row.Children.Add(channel);
        }

        return row;
    }

    private static Button NoticeButton(FolderEntry folder)
    {
        var button = new Button
        {
            Width = 22,
            Height = 22,
            MinHeight = 0,
            Padding = new Thickness(0),
            Content = new FontIcon { Glyph = folder.NoticeGlyph, FontSize = 13, Foreground = Brush("TextFillColorSecondaryBrush") },
            Resources =
            {
                ["ButtonBackground"] = Brush("SubtleFillColorTransparentBrush"),
                ["ButtonBackgroundPointerOver"] = Brush("SubtleFillColorSecondaryBrush"),
                ["ButtonBackgroundPressed"] = Brush("SubtleFillColorTertiaryBrush"),
            },
        };
        ToolTipService.SetToolTip(button, folder.Notice!.Text);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, folder.Notice.Text);
        return button;
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
