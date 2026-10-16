using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.Logging;

using Steward.App.Services;
using Steward.App.ViewModels;
using Steward.Core;

namespace Steward.Desktop.Views;

public sealed partial class AddonFoldersDialog : FAContentDialog
{
    private readonly IUiPlatform _platform;

    public AddonFoldersDialog(string title, IReadOnlyList<FolderEntry> folders, IUiPlatform platform, ILogger logger)
    {
        _platform = platform;
        InitializeComponent();
        Title = title;
        Caption.Text = $"Also installed with {title}:";
        RowsLoaded = Task.WhenAll(folders.Select(folder => AddRowAsync(folder, logger)));
    }

    public Task RowsLoaded { get; }

    public FolderEntry? ChangelogRequest { get; private set; }

    public void ClearChangelogRequest() => ChangelogRequest = null;

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    private async Task AddRowAsync(FolderEntry folder, ILogger logger)
    {
        var slot = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Rows.Children.Add(slot);
        var addon = await Task.Run(() => LocalAddons.Read(folder.AddOnsPath, folder.Folder, folder.ClientInterface)).ConfigureAwait(true);
        slot.Content = addon is null
            ? Row(Glyph("Folder", 15), null, folder, null)
            : Row(await Tile(_platform, folder, addon.Name, logger).ConfigureAwait(true), addon.Name, folder, addon);
    }

    private Control VersionCell(FolderEntry folder, LocalAddon addon)
    {
        var shortVersion = VersionLabel.For(addon.Version, folder.Folder);
        var text = new TextBlock
        {
            Text = shortVersion,
            FontFamily = Resource<FontFamily>("MonoFontFamily")!,
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2),
            Foreground = Brush("TextFillColorSecondaryBrush"),
        };
        var full = addon.Version is not null && addon.Version != shortVersion ? addon.Version : null;
        if (folder.Changelog is not { Count: > 0 })
        {
            if (full is not null)
            {
                ToolTip.SetTip(text, full);
            }

            return text;
        }

        var icon = Glyph("BulletedList", 12);
        icon.Margin = new Thickness(0, 1, 0, 0);
        Grid.SetColumn(icon, 1);
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 4, Children = { text, icon } };

        var button = new Button
        {
            Theme = Resource<Avalonia.Styling.ControlTheme>("SubtleButtonStyle"),
            HorizontalContentAlignment = HorizontalAlignment.Right,
            MinHeight = 0,
            Margin = new Thickness(0, 0, -4, 0),
            Padding = new Thickness(4, 2, 4, 2),
            Content = content,
        };
        ToolTip.SetTip(button, string.Join("\n", new[] { full, "Changelog" }.OfType<string>()));
        AutomationProperties.SetName(button, $"Changelog for {addon.Name}");
        button.Click += (_, _) =>
        {
            ChangelogRequest = folder;
            Hide();
        };
        return button;
    }

    private static async Task<Control> Tile(IUiPlatform platform, FolderEntry folder, string name, ILogger logger)
    {
        var tile = new Grid();
        tile.Children.Add(new TextBlock
        {
            Text = InitialsTile.Text(name),
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromUInt32(InitialsTile.Color(folder.Folder))),
        });
        if (await LocalAddonIcon.LoadAsync(platform, folder.AddOnsPath, folder.Folder, logger).ConfigureAwait(true) is Avalonia.Media.IImage icon)
        {
            tile.Children.Add(new Image { Source = icon, Stretch = Stretch.UniformToFill });
        }

        return tile;
    }

    private Grid Row(Control icon, string? name, FolderEntry folder, LocalAddon? addon)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,180,108"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 12, RowSpacing = 2 };

        var tile = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center,
            Background = name is null ? null : Brush("ChipBrush"),
            Child = icon,
        };
        Grid.SetRowSpan(tile, 2);
        row.Children.Add(tile);

        if (name is not null)
        {
            var title = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 13.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (!string.IsNullOrEmpty(folder.Notice?.Text))
            {
                var notice = NoticeButton(folder);
                Grid.SetColumn(notice, 1);
                title.Children.Add(notice);
            }

            Grid.SetColumn(title, 1);
            row.Children.Add(title);
        }

        var folderName = new SelectableTextBlock
        {
            Text = folder.Folder,
            FontFamily = Resource<FontFamily>("MonoFontFamily")!,
            FontSize = name is null ? 12 : 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush(name is null ? "TextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush"),
        };
        Grid.SetColumn(folderName, 1);
        Grid.SetRow(folderName, name is null ? 0 : 1);
        Grid.SetRowSpan(folderName, name is null ? 2 : 1);
        row.Children.Add(folderName);

        var stack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        if (addon is not null)
        {
            var version = VersionCell(folder, addon);
            version.HorizontalAlignment = HorizontalAlignment.Right;
            stack.Children.Add(version);
        }

        if (folder.Released is not null)
        {
            var released = new TextBlock
            {
                Text = folder.Released,
                FontSize = 11.5,
                HorizontalAlignment = HorizontalAlignment.Right,
                Foreground = Brush("TextFillColorTertiaryBrush"),
            };
            ToolTip.SetTip(released, folder.ReleasedTip);
            stack.Children.Add(released);
        }

        if (stack.Children.Count > 0)
        {
            Grid.SetColumn(stack, 2);
            Grid.SetRowSpan(stack, 2);
            row.Children.Add(stack);
        }

        if (folder.Channel is not null)
        {
            var channel = new TextBlock
            {
                Text = folder.Channel,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                Foreground = Brush("TextFillColorSecondaryBrush"),
            };
            Grid.SetColumn(channel, 3);
            Grid.SetRowSpan(channel, 2);
            row.Children.Add(channel);
        }

        return row;
    }

    private static Button NoticeButton(FolderEntry folder)
    {
        var glyph = Glyph(folder.NoticeGlyph, 13);
        var button = new Button
        {
            Theme = Resource<Avalonia.Styling.ControlTheme>("SubtleButtonStyle"),
            Width = 20,
            Height = 22,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Content = glyph,
        };
        ToolTip.SetTip(button, folder.Notice!.Text);
        AutomationProperties.SetName(button, folder.Notice.Text);
        return button;
    }

    private static TextBlock Glyph(string name, double size) => new()
    {
        Classes = { "Glyph" },
        Text = Resource<string>($"Glyph.{name}"),
        FontSize = size,
        Foreground = Brush("TextFillColorSecondaryBrush"),
    };

    private static IBrush Brush(string key) => Resource<IBrush>(key)!;

    private static T? Resource<T>(string key) where T : class =>
        Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) == true ? value as T : null;
}
