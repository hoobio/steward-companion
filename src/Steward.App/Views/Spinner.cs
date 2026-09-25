using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

using Windows.Foundation;

namespace Steward.App.Views;

public static class Spinner
{
    private static readonly TimeSpan Turn = TimeSpan.FromSeconds(1);

    public static readonly DependencyProperty IsSpinningProperty = DependencyProperty.RegisterAttached(
        "IsSpinning", typeof(bool), typeof(Spinner), new PropertyMetadata(false, OnIsSpinningChanged));

    private static readonly DependencyProperty StoryboardProperty = DependencyProperty.RegisterAttached(
        "Storyboard", typeof(Storyboard), typeof(Spinner), new PropertyMetadata(null));

    public static bool GetIsSpinning(UIElement element) => (bool)element.GetValue(IsSpinningProperty);

    public static void SetIsSpinning(UIElement element, bool value) => element.SetValue(IsSpinningProperty, value);

    private static void OnIsSpinningChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var element = (UIElement)sender;
        if (element.RenderTransform is not RotateTransform rotation)
        {
            rotation = new RotateTransform();
            element.RenderTransform = rotation;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        var previous = element.GetValue(StoryboardProperty) as Storyboard;
        var angle = previous is null ? 0 : previous.GetCurrentTime().TotalSeconds % Turn.TotalSeconds / Turn.TotalSeconds * 360;
        previous?.Stop();

        var storyboard = (bool)args.NewValue
            ? Rotate(rotation, 0, Turn, RepeatBehavior.Forever)
            : Rotate(rotation, angle, Turn * ((360 - angle) / 360), new RepeatBehavior(1));
        element.SetValue(StoryboardProperty, storyboard);
        storyboard.Begin();
    }

    private static Storyboard Rotate(RotateTransform rotation, double from, TimeSpan duration, RepeatBehavior repeat)
    {
        var animation = new DoubleAnimation { From = from, To = 360, Duration = duration };
        Storyboard.SetTarget(animation, rotation);
        Storyboard.SetTargetProperty(animation, nameof(RotateTransform.Angle));
        var storyboard = new Storyboard { RepeatBehavior = repeat };
        storyboard.Children.Add(animation);
        return storyboard;
    }
}
