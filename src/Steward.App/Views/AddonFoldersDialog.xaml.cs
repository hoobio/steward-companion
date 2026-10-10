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
            ? Row(new FontIcon { Glyph = "", FontSize = 15, Foreground = Brush("TextFillColorSecondaryBrush") }, null, folder.Folder, null)
            : WithChangelog(Row(await Tile(folder, addon.Name, logger).ConfigureAwait(true), addon.Name, folder.Folder, addon.Version), addon, folder);
    }

    public FolderEntry? ChangelogRequest { get; private set; }

    public void ClearChangelogRequest() => ChangelogRequest = null;

    private Grid WithChangelog(Grid row, LocalAddon addon, FolderEntry folder)
    {
        if (folder.Changelog is not { Count: > 0 })
        {
            return row;
        }

        var button = new Button
        {
            Width = 22,
            Height = 22,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon { Glyph = "", FontSize = 12, Foreground = Brush("TextFillColorSecondaryBrush") },
            Resources =
            {
                ["ButtonBackground"] = Brush("SubtleFillColorTransparentBrush"),
                ["ButtonBackgroundPointerOver"] = Brush("SubtleFillColorSecondaryBrush"),
                ["ButtonBackgroundPressed"] = Brush("SubtleFillColorTertiaryBrush"),
            },
        };
        ToolTipService.SetToolTip(button, $"Changelog for {addon.Name}");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Changelog for {addon.Name}");
        button.Click += (_, _) =>
        {
            ChangelogRequest = folder;
            Hide();
        };

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(button, 3);
        row.Children.Add(button);
        return row;
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

    private static Grid Row(UIElement icon, string? name, string folder, string? version)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

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
            lines.Children.Add(new TextBlock { Text = name, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis });
        }

        lines.Children.Add(new TextBlock
        {
            Text = folder,
            FontFamily = new FontFamily("Cascadia Mono"),
            FontSize = name is null ? 12 : 11,
            IsTextSelectionEnabled = true,
            Foreground = Brush(name is null ? "TextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush"),
        });
        Grid.SetColumn(lines, 1);
        row.Children.Add(lines);

        if (version is not null)
        {
            var text = new TextBlock
            {
                Text = version,
                FontFamily = new FontFamily("Cascadia Mono"),
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush("TextFillColorSecondaryBrush"),
            };
            Grid.SetColumn(text, 2);
            row.Children.Add(text);
        }

        return row;
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
