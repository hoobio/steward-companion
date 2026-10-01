using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Steward.Core;

namespace Steward.App.Views;

public static class ChangelogView
{
    private const double IndentPerDepth = 16;

    public static UIElement Build(IReadOnlyList<ChangelogBlock> blocks)
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (var block in blocks)
        {
            var element = BuildBlock(block, first: panel.Children.Count == 0);
            element.Opacity = block.Muted ? 0.5 : 1;
            panel.Children.Add(element);
        }

        return panel;
    }

    private static UIElement BuildBlock(ChangelogBlock block, bool first)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["BodyTextBlockStyle"] };
        foreach (var run in block.Runs ?? [])
        {
            text.Inlines.Add(BuildRun(run));
        }

        switch (block.Kind)
        {
            case "heading":
                text.FontWeight = FontWeights.SemiBold;
                text.FontSize = block.Level switch { 1 => 20, 2 => 16, _ => text.FontSize };
                text.Margin = new Thickness(0, first ? 0 : 6, 0, 0);
                return text;
            case "item":
                var row = new Grid { ColumnSpacing = 8, Margin = new Thickness(IndentPerDepth * Math.Max(block.Depth, 0), 0, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock
                {
                    Text = "•",
                    Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                });
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                return row;
            default:
                return text;
        }
    }

    private static Inline BuildRun(ChangelogRun run)
    {
        var plain = new Run { Text = run.Text };
        if (!Uri.TryCreate(run.Href, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return plain;
        }

        var link = new Hyperlink { NavigateUri = uri };
        link.Inlines.Add(plain);
        return link;
    }
}
