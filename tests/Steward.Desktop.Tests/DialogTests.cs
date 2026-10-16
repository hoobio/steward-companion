using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAvalonia.UI.Controls;

using Steward.Desktop.Services;

namespace Steward.Desktop.Tests;

public sealed class DialogTests(AvaloniaSession session)
{
    [Fact]
    public void Dialogs_are_440_wide_with_eight_pixel_corners_the_raised_fill_and_an_accent_primary() => session.Run(() =>
    {
        var window = new Window { Width = 1280, Height = 820 };
        window.Show();
        var dialog = new FAContentDialog { Title = "Title", Content = "Body", PrimaryButtonText = "Go", CloseButtonText = "Cancel" };

        var shown = AppDialogs.ShowAsync(dialog, window);
        AvaloniaSession.WaitFor(() => dialog.IsLoaded);
        window.CaptureRenderedFrame();

        var background = dialog.GetVisualDescendants().OfType<Border>().First(border => border.Name == "BackgroundElement");
        var primary = dialog.GetVisualDescendants().OfType<Button>().First(button => button.Name == "PrimaryButton");
        Assert.True(Application.Current!.TryGetResource("AccentButtonStyle", null, out var accent));
        var raised = Assert.IsAssignableFrom<ISolidColorBrush>(background.Background);
        var bounds = background.Bounds;
        var corners = background.CornerRadius;
        AvaloniaSession.CloseDialog(dialog, shown);
        window.Close();

        Assert.Equal(440, bounds.Width);
        Assert.Equal(new CornerRadius(8), corners);
        Assert.Equal(Color.Parse("#FF2B3140"), raised.Color);
        Assert.Same((ControlTheme)accent!, primary.Theme);
    });

    [Fact]
    public void A_dialog_hidden_while_queued_is_never_shown_and_returns_none() => session.Run(() =>
    {
        var window = new Window { Width = 1280, Height = 820 };
        window.Show();
        var first = new FAContentDialog { Title = "First", CloseButtonText = "Close" };
        var queued = new FAContentDialog { Title = "Queued", CloseButtonText = "Close" };
        var firstShown = AppDialogs.ShowAsync(first, window);
        AvaloniaSession.WaitFor(() => first.IsLoaded);
        var opened = false;
        queued.Opened += (_, _) => opened = true;
        var queuedShown = AppDialogs.ShowAsync(queued, window);

        queued.Hide();
        Assert.True(queued.IsHitTestVisible);
        AvaloniaSession.CloseDialog(first, firstShown);
        AvaloniaSession.WaitFor(() => queuedShown.IsCompleted);
        window.Close();

        Assert.Equal(FAContentDialogResult.None, queuedShown.Result);
        Assert.False(opened);
        Assert.Null(queued.Parent);
    });
}
