using Avalonia;
using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Styling;

namespace Steward.Desktop.Views;

public sealed class Spinner : AvaloniaObject
{
    private static readonly TimeSpan Turn = TimeSpan.FromSeconds(1);

    public static readonly AttachedProperty<bool> IsSpinningProperty =
        AvaloniaProperty.RegisterAttached<Spinner, Visual, bool>("IsSpinning");

    private static readonly AttachedProperty<CancellationTokenSource?> RunProperty =
        AvaloniaProperty.RegisterAttached<Spinner, Visual, CancellationTokenSource?>("Run");

    static Spinner() => IsSpinningProperty.Changed.AddClassHandler<Visual>(OnIsSpinningChanged);

    public static bool GetIsSpinning(Visual element) => element.GetValue(IsSpinningProperty);

    public static void SetIsSpinning(Visual element, bool value) => element.SetValue(IsSpinningProperty, value);

    private static void OnIsSpinningChanged(Visual element, AvaloniaPropertyChangedEventArgs args)
    {
        if (element.RenderTransform is not RotateTransform rotation)
        {
            rotation = new RotateTransform();
            element.RenderTransform = rotation;
            element.RenderTransformOrigin = RelativePoint.Center;
        }

        var angle = rotation.Angle % 360;
        element.GetValue(RunProperty)?.Cancel();
        rotation.Angle = angle;
        var run = new CancellationTokenSource();
        element.SetValue(RunProperty, run);

        if (args.NewValue is not true && angle == 0)
        {
            return;
        }

        _ = SpinAsync(element, angle, args.NewValue is true, run.Token);
    }

    // Animation.RunAsync rejects IterationCount.Infinite, so a spin is repeated one-turn runs.
    private static async Task SpinAsync(Visual element, double angle, bool loop, CancellationToken token)
    {
        try
        {
            // TransformAnimator casts its target to Visual and animates that visual's RenderTransform, so the transform itself is never the target.
            if (!loop)
            {
                await Rotate(angle, 360, Turn * ((360 - angle) / 360)).RunAsync(element, token);
                return;
            }

            while (!token.IsCancellationRequested)
            {
                await Rotate(angle, angle + 360, Turn).RunAsync(element, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static Animation Rotate(double from, double to, TimeSpan duration) => new()
    {
        Duration = duration,
        IterationCount = new IterationCount(1),
        FillMode = FillMode.Forward,
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(RotateTransform.AngleProperty, from) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(RotateTransform.AngleProperty, to) } },
        },
    };
}
