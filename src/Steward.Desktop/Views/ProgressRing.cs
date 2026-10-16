using System.Diagnostics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Steward.Desktop.Views;

public sealed class ProgressRing : Control
{
    public static readonly StyledProperty<bool> IsActiveProperty = AvaloniaProperty.Register<ProgressRing, bool>(nameof(IsActive), true);

    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(Foreground));

    private static readonly double Period = TimeSpan.FromSeconds(2).TotalSeconds;
    private static readonly long Origin = Stopwatch.GetTimestamp();

    static ProgressRing()
    {
        AffectsRender<ProgressRing>(IsActiveProperty, ForegroundProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<ProgressRing>(false);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    // WinUI's ProgressRingIndeterminate Lottie: over 2s the head runs 0 to 180 degrees while the tail holds, then the tail catches up, all turning 900 degrees.
    public static (double Start, double Sweep) Frame(double progress)
    {
        var half = progress < 0.5;
        var eased = half ? progress * 2 : progress * 2 - 1;
        var rotation = (half ? 0 : 450) + 450 * eased;
        return half ? (rotation, 180 * eased) : (rotation + 180 * eased, 180 * (1 - eased));
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (!IsActive || Foreground is not { } foreground || size <= 0)
        {
            return;
        }

        var (start, sweep) = Frame(Stopwatch.GetElapsedTime(Origin).TotalSeconds % Period / Period);
        var radius = size * 35 / 80;
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        Point At(double degrees) => centre + new Vector(Math.Sin(degrees * Math.PI / 180), -Math.Cos(degrees * Math.PI / 180)) * radius;

        var arc = new StreamGeometry();
        using (var figure = arc.Open())
        {
            figure.BeginFigure(At(start), false);
            figure.ArcTo(At(start + Math.Max(sweep, 0.036)), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            figure.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(foreground, size * 7.5 / 80, lineCap: PenLineCap.Round), arc);
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ => InvalidateVisual());
    }
}
