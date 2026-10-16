using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;

using Steward.Core;

namespace Steward.Desktop.Views;

public sealed class ChangelogView : StackPanel
{
    private const double IndentPerDepth = 16;

    public static readonly StyledProperty<IReadOnlyList<ChangelogBlock>?> BlocksProperty =
        AvaloniaProperty.Register<ChangelogView, IReadOnlyList<ChangelogBlock>?>(nameof(Blocks));

    static ChangelogView() => BlocksProperty.Changed.AddClassHandler<ChangelogView>((view, _) => view.Rebuild());

    public ChangelogView() => Spacing = 6;

    public IReadOnlyList<ChangelogBlock>? Blocks
    {
        get => GetValue(BlocksProperty);
        set => SetValue(BlocksProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(StackPanel);

    private void Rebuild()
    {
        Children.Clear();
        foreach (var block in Blocks ?? [])
        {
            var element = BuildBlock(block, first: Children.Count == 0);
            element.Opacity = block.Muted ? 0.5 : 1;
            Children.Add(element);
        }
    }

    private static Control BuildBlock(ChangelogBlock block, bool first)
    {
        var text = BuildText(block.Runs ?? []);
        text.Classes.Add("Body");
        text.TextWrapping = TextWrapping.Wrap;

        switch (block.Kind)
        {
            case "heading":
                text.FontWeight = FontWeight.SemiBold;
                if (block.Level == 1)
                {
                    text.FontSize = 20;
                    text.FontFamily = (FontFamily)Resource("DisplayFontFamily")!;
                }
                else if (block.Level == 2)
                {
                    text.FontSize = 16;
                }

                text.Margin = new Thickness(0, first ? 0 : 6, 0, 0);
                return text;
            case "item":
                var bullet = new TextBlock { Text = "•", Foreground = (IBrush)Resource("TextFillColorSecondaryBrush")! };
                bullet.Classes.Add("Body");
                Grid.SetColumn(text, 1);
                return new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    ColumnSpacing = 8,
                    Margin = new Thickness(IndentPerDepth * Math.Max(block.Depth, 0), 0, 0, 0),
                    Children = { bullet, text },
                };
            default:
                return text;
        }
    }

    private static LinkTextBlock BuildText(IReadOnlyList<ChangelogRun> runs)
    {
        var links = new List<(int Start, int End, Uri Uri)>();
        var position = 0;
        var inlines = new List<Inline>();
        foreach (var run in runs)
        {
            var plain = new Run { Text = run.Text };
            if (Uri.TryCreate(run.Href, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                plain.Foreground = (IBrush)Resource("AccentTextFillColorPrimaryBrush")!;
                plain.TextDecorations = TextDecorations.Underline;
                links.Add((position, position + run.Text.Length, uri));
            }

            position += run.Text.Length;
            inlines.Add(plain);
        }

        var text = new LinkTextBlock(links);
        text.Inlines!.AddRange(inlines);
        return text;
    }

    private static object? Resource(string key) =>
        Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) == true ? value : null;

    private sealed class LinkTextBlock(IReadOnlyList<(int Start, int End, Uri Uri)> links) : TextBlock
    {
        protected override Type StyleKeyOverride => typeof(TextBlock);

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (links.Count > 0)
            {
                Cursor = LinkAt(e) is null ? null : new Cursor(StandardCursorType.Hand);
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (links.Count > 0 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && LinkAt(e) is { } uri)
            {
                _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(uri);
                e.Handled = true;
            }
        }

        private Uri? LinkAt(PointerEventArgs e)
        {
            var hit = TextLayout.HitTestPoint(e.GetPosition(this));
            return hit.IsInside
                ? links.Where(link => hit.TextPosition >= link.Start && hit.TextPosition < link.End).Select(link => link.Uri).FirstOrDefault()
                : null;
        }
    }
}
