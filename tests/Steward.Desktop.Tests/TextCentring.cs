using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAvalonia.UI.Controls;

namespace Steward.Desktop.Tests;

internal static class TextCentring
{
    public const string Caps = "HXE";

    private static readonly Type[] ControlBoxes =
    [
        typeof(Button), typeof(ListBoxItem), typeof(ComboBox), typeof(MenuItem), typeof(TextBox), typeof(ToolTip), typeof(FAInfoBar), typeof(FAInfoBadge),
    ];

    public static void Verify(TopLevel top, string screen)
    {
        var findings = Scan(top, screen);
        if (Environment.GetEnvironmentVariable("STEWARD_TEXT_CENTRING_PNG_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.AppendAllLines(System.IO.Path.Combine(directory, "walk.txt"), findings.Select(finding => finding.ToString()), Encoding.UTF8);
        }

        var failures = findings.Where(finding => finding.Fails).ToList();
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    public static IReadOnlyList<Finding> Scan(TopLevel top, string screen)
    {
        Dispatcher.UIThread.RunJobs();
        var candidates = new List<(TextBlock Text, Control Box, string Original)>();
        var findings = new List<Finding>();
        var root = top.GetVisualDescendants().OfType<FAContentDialog>().LastOrDefault(dialog => dialog.IsEffectivelyVisible) as Visual ?? top;
        foreach (var text in root.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!IsCandidate(text, top))
            {
                continue;
            }

            if (Box(text) is not { } box)
            {
                continue;
            }

            if (Rect(text, top) is var line && Rect(box, top) is var outline && (line.Bottom <= outline.Y || line.Y >= outline.Bottom))
            {
                findings.Add(new Finding(screen, text.Text!, Describe(box), 0, 0, "wrapped onto a row below its icon"));
                continue;
            }

            if (Visible(box, top).Height < box.Bounds.Height - 0.5)
            {
                findings.Add(new Finding(screen, text.Text!, Describe(box), 0, 0, "box clipped by a scroller or the window"));
                continue;
            }

            if (box.Bounds.Height > text.TextLayout.Height * 3)
            {
                findings.Add(new Finding(screen, text.Text!, Describe(box), 0, 0, "box taller than three lines, not a single-line control"));
                continue;
            }

            if (Stacked(text, box, top))
            {
                findings.Add(new Finding(screen, text.Text!, Describe(box), 0, 0, "stacked with other lines in its box"));
                continue;
            }

            candidates.Add((text, box, text.Text!));
        }

        foreach (var (text, _, _) in candidates)
        {
            text.SetCurrentValue(TextBlock.TextProperty, Caps);
        }

        var carets = root.GetVisualDescendants().OfType<TextBox>().Select(box => (Box: box, Brush: box.CaretBrush)).ToList();
        foreach (var (box, _) in carets)
        {
            box.SetCurrentValue(TextBox.CaretBrushProperty, Brushes.Transparent);
        }

        Dispatcher.UIThread.RunJobs();
        try
        {
            using var frame = top.CaptureRenderedFrame()!;
            using var locked = frame.Lock();
            var pixels = new byte[locked.RowBytes * locked.Size.Height];
            Marshal.Copy(locked.Address, pixels, 0, pixels.Length);
            var size = locked.Size;
            uint Pixel(int x, int y) => BitConverter.ToUInt32(pixels, (y * locked.RowBytes) + (x * 4));

            foreach (var (text, box, original) in candidates)
            {
                var boxRect = Rect(box, top);
                var textRect = Visible(text, top);
                var top0 = Math.Max(0, (int)Math.Round(boxRect.Y));
                var bottom = Math.Min(size.Height, (int)Math.Round(boxRect.Bottom));
                var left = Math.Max(0, (int)Math.Ceiling(textRect.X));
                var right = Math.Min(size.Width, (int)Math.Floor(textRect.Right));
                if (right - left < 2 || bottom - top0 < 2)
                {
                    findings.Add(new Finding(screen, original, Describe(box), 0, 0, "clipped"));
                    continue;
                }

                var fill = Enumerable.Range(top0, bottom - top0)
                    .SelectMany(y => Enumerable.Range(left, right - left).Select(x => Pixel(x, y)))
                    .GroupBy(colour => colour).MaxBy(group => group.Count())!.Key;
                var rows = Enumerable.Range(top0, bottom - top0)
                    .Select(y => (y, count: Enumerable.Range(left, right - left).Count(x => Distance(Pixel(x, y), fill) > 48)))
                    .Where(row => row.count > 0 && row.count < (right - left) * 0.9)
                    .Select(row => row.y)
                    .ToList();
                if (rows.Count == 0)
                {
                    findings.Add(new Finding(screen, original, Describe(box), 0, 0, "no ink against its fill"));
                    continue;
                }

                findings.Add(new Finding(screen, original, string.Create(CultureInfo.InvariantCulture, $"{Describe(box)} {bottom - top0}px, text {text.FontSize}px"), rows.Min() - top0, bottom - rows.Max() - 1, null));
            }
        }
        finally
        {
            foreach (var (text, _, original) in candidates)
            {
                text.SetCurrentValue(TextBlock.TextProperty, original);
            }

            foreach (var (box, brush) in carets)
            {
                box.SetCurrentValue(TextBox.CaretBrushProperty, brush);
            }

            Dispatcher.UIThread.RunJobs();
        }

        return findings;
    }

    private static bool IsCandidate(TextBlock text, TopLevel top) =>
        text.IsEffectivelyVisible
        && text.Text is { Length: > 0 }
        && text.Inlines is not { Count: > 0 }
        && !text.Classes.Contains("Glyph")
        && !text.FontFamily.Name.Contains("Icons", StringComparison.OrdinalIgnoreCase)
        && text.Bounds is { Width: > 0, Height: > 0 }
        && text.TextLayout.TextLines.Count == 1
        && text.GetSelfAndVisualAncestors().OfType<Visual>().All(visual => visual.Opacity > 0)
        && Visible(text, top) is { Width: > 1, Height: > 1 };

    private static Control? Box(TextBlock text)
    {
        foreach (var ancestor in text.GetVisualAncestors().OfType<Control>())
        {
            if (ancestor is TopLevel or UserControl or FAContentDialog or OverlayLayer)
            {
                return null;
            }

            if (text.TemplatedParent is FAInfoBar bar && text.Name is "Title" or "Message")
            {
                return bar.GetVisualDescendants().OfType<Control>().FirstOrDefault(part => part.Name == "IconBackground" && part.IsEffectivelyVisible);
            }

            if ((ancestor as ToggleSwitch ?? (ancestor as Panel)?.Children.OfType<ToggleSwitch>().FirstOrDefault(toggle => toggle.IsEffectivelyVisible)) is { } toggleSwitch)
            {
                return toggleSwitch.GetVisualDescendants().OfType<Control>().FirstOrDefault(part => part.Name == "OuterBorder");
            }

            if (ancestor is CheckBox or RadioButton)
            {
                return ancestor.GetVisualDescendants().OfType<Control>().FirstOrDefault(part => part is Ellipse || part.Name == "NormalRectangle");
            }

            if (ancestor.Name is "LayoutRoot" or "PART_LayoutRoot" || ControlBoxes.Any(type => type.IsInstanceOfType(ancestor)) || IsFilled(ancestor))
            {
                return ancestor;
            }
        }

        return null;
    }

    private static bool IsFilled(Control control) => (control switch
    {
        Border border => border.Background,
        Panel panel => panel.Background,
        ContentPresenter presenter => presenter.Background,
        TemplatedControl templated => templated.Background,
        _ => null,
    }) is { } brush && brush.Opacity > 0 && brush is not ISolidColorBrush { Color.A: 0 };

    private static bool Stacked(TextBlock text, Control box, TopLevel top)
    {
        var line = Rect(text, top);
        var ancestors = text.GetVisualAncestors().ToHashSet();
        var scope = !ancestors.Contains(box) && box.TemplatedParent is Control owner && ancestors.Contains(owner) ? owner : box;
        return scope.GetVisualDescendants().OfType<Control>()
            .Where(other => scope == box || (other != box && !box.GetVisualDescendants().Contains(other)))
            .Where(other => other != text && !ancestors.Contains(other) && other.IsEffectivelyVisible && other.Bounds is { Width: > 0, Height: > 0 })
            .Where(other => other is TextBlock or Image or Shape or TextPresenter || !other.GetVisualChildren().Any())
            .Where(other => !(other is TextBlock found && found.GetVisualAncestors().Contains(text)))
            .Select(other => Rect(other, top))
            .Any(other => other.Right > line.X && other.X < line.Right && (other.Bottom <= line.Y + 1 || other.Y >= line.Bottom - 1));
    }

    private static Rect Rect(Visual visual, TopLevel top) =>
        visual.TranslatePoint(default, top) is { } origin ? new Rect(origin, visual.Bounds.Size) : default;

    private static Rect Visible(Visual visual, TopLevel top)
    {
        var rect = Rect(visual, top).Intersect(new Rect(top.ClientSize));
        foreach (var clip in visual.GetVisualAncestors().OfType<Visual>().Where(ancestor => ancestor.ClipToBounds))
        {
            rect = rect.Intersect(Rect(clip, top));
        }

        return rect;
    }

    private static string Describe(Control box) =>
        string.IsNullOrEmpty(box.Name) ? box.GetType().Name : $"{box.GetType().Name}#{box.Name}";

    private static int Distance(uint a, uint b) =>
        Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF)) + Math.Abs((int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF)) + Math.Abs((int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF));

    internal sealed record Finding(string Screen, string Text, string Box, int Above, int Below, string? Skipped)
    {
        public bool Fails => Skipped is null && Math.Abs(Above - Below) > 1;

        public override string ToString() => Skipped is null
            ? string.Create(CultureInfo.InvariantCulture, $"{Screen}\t{Text}\t{Box}\tabove {Above}, below {Below}{(Fails ? "\tFAIL" : "")}")
            : $"{Screen}\t{Text}\t{Box}\tskipped: {Skipped}";
    }
}
