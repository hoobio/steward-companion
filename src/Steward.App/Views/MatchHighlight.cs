using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

using Steward.Core;

namespace Steward.App.Views;

public static class MatchHighlight
{
    public static readonly DependencyProperty MatchProperty = DependencyProperty.RegisterAttached(
        "Match", typeof(object), typeof(MatchHighlight), new PropertyMetadata(null, OnMatchChanged));

    public static object? GetMatch(TextBlock element) => element.GetValue(MatchProperty);

    public static void SetMatch(TextBlock element, object? value) => element.SetValue(MatchProperty, value);

    private static void OnMatchChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var text = (TextBlock)sender;
        text.TextHighlighters.Clear();
        if (args.NewValue is not SearchMatch match)
        {
            return;
        }

        var highlighter = new TextHighlighter
        {
            Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            Background = new SolidColorBrush(Colors.Transparent),
        };
        highlighter.Ranges.Add(new TextRange { StartIndex = match.Start, Length = match.Length });
        text.TextHighlighters.Add(highlighter);
    }
}
