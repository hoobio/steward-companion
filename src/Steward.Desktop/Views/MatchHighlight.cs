using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;

using Steward.Core;

namespace Steward.Desktop.Views;

public static class MatchHighlight
{
    public static readonly AttachedProperty<object?> MatchProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, object?>("Match", typeof(MatchHighlight));

    private static readonly AttachedProperty<bool> RebuildingProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>("Rebuilding", typeof(MatchHighlight));

    static MatchHighlight()
    {
        MatchProperty.Changed.AddClassHandler<TextBlock>((text, _) => Apply(text));
        TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((text, _) => Apply(text));
    }

    public static object? GetMatch(TextBlock element) => element.GetValue(MatchProperty);

    public static void SetMatch(TextBlock element, object? value) => element.SetValue(MatchProperty, value);

    private static void Apply(TextBlock text)
    {
        if (text.GetValue(RebuildingProperty))
        {
            return;
        }

        text.SetValue(RebuildingProperty, true);
        try
        {
            var whole = text.Inlines is { Count: > 0 } ? string.Concat(text.Inlines.OfType<Run>().Select(run => run.Text)) : text.Text ?? "";
            if (GetMatch(text) is not SearchMatch match || match.Start < 0 || match.Start + match.Length > whole.Length)
            {
                if (text.Inlines is { Count: > 0 })
                {
                    text.Inlines.Clear();
                    text.Text = whole;
                }

                return;
            }

            text.Inlines ??= [];
            text.Text = null;
            text.Inlines.Clear();
            text.Inlines.Add(new Run(whole[..match.Start]));
            var hit = new Run(whole.Substring(match.Start, match.Length));
            hit.Bind(TextElement.ForegroundProperty, text.GetResourceObservable("AccentTextFillColorPrimaryBrush"));
            text.Inlines.Add(hit);
            text.Inlines.Add(new Run(whole[(match.Start + match.Length)..]));
        }
        finally
        {
            text.SetValue(RebuildingProperty, false);
        }
    }
}
