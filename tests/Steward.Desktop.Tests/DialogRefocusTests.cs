using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAvalonia.UI.Controls;

using Steward.Desktop.Services;

namespace Steward.Desktop.Tests;

public sealed class DialogRefocusTests(AvaloniaSession session)
{
    [Fact]
    public void Focus_returns_to_cancel_when_the_window_is_deactivated_and_reactivated() => session.Run(() =>
    {
        var (window, dialog, shown) = Open();
        Assert.Equal("CloseButton", FocusedName(window));

        Raise(window, "LostFocus");
        Raise(window, "Deactivated");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(window.FocusManager!.GetFocusedElement());
        Assert.False(window.IsActive);

        Raise(window, "Activated");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("CloseButton", FocusedName(window));
        Close(window, dialog, shown);
    });

    [Fact]
    public void Focus_returns_to_cancel_after_another_window_held_focus() => session.Run(() =>
    {
        var (window, dialog, shown) = Open();
        var other = new Window { Content = new TextBox() };
        other.Show();
        Dispatcher.UIThread.RunJobs();
        ((TextBox)other.Content).Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<TextBox>(other.FocusManager!.GetFocusedElement());

        window.Activate();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("CloseButton", FocusedName(window));
        other.Close();
        Close(window, dialog, shown);
    });

    // The platform callbacks are not public, and Win32 raises LostFocus (WM_KILLFOCUS) and Deactivated (WM_ACTIVATE) where the headless Hide raises only Deactivated.
    private static void Raise(Window window, string callback) =>
        ((Action?)window.PlatformImpl!.GetType().GetProperty(callback)!.GetValue(window.PlatformImpl))!.Invoke();

    private static string? FocusedName(Window window) => (window.FocusManager!.GetFocusedElement() as Control)?.Name;

    private static (Window Window, FAContentDialog Dialog, Task<FAContentDialogResult> Shown) Open()
    {
        var window = new Window { Width = 1280, Height = 820, Content = new Button { Content = "Behind" } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var dialog = new FAContentDialog
        {
            Title = "Install DBM?",
            Content = "DBM 12.1.14 on Forever",
            PrimaryButtonText = "Install",
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.None,
        };
        var shown = AppDialogs.ShowAsync(dialog, window);
        AvaloniaSession.WaitFor(() => dialog.IsLoaded);
        var settled = DateTime.UtcNow.AddMilliseconds(300);
        AvaloniaSession.WaitFor(() => DateTime.UtcNow > settled);
        Assert.True(dialog.IsVisualAncestorOf((Control)window.FocusManager!.GetFocusedElement()!));
        return (window, dialog, shown);
    }

    private static void Close(Window window, FAContentDialog dialog, Task<FAContentDialogResult> shown)
    {
        AvaloniaSession.CloseDialog(dialog, shown);
        window.Close();
    }
}
