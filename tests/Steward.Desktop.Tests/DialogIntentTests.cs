using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAvalonia.UI.Controls;

using Steward.Desktop.Services;

namespace Steward.Desktop.Tests;

public sealed class DialogIntentTests(AvaloniaSession session)
{
    [Fact]
    public void A_space_key_up_with_no_key_down_does_not_confirm() => session.Run(() =>
    {
        var (window, dialog, shown) = Open();

        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");

        Assert.Equal(FAContentDialogResult.None, Finish(window, dialog, shown));
    });

    [Fact]
    public void A_stray_space_press_at_open_does_not_confirm() => session.Run(() =>
    {
        var (window, dialog, shown) = Open();

        Press(window, Key.Space, PhysicalKey.Space);

        Assert.Equal(FAContentDialogResult.None, Finish(window, dialog, shown));
    });

    [Fact]
    public void A_stray_enter_press_at_open_does_not_confirm() => session.Run(() =>
    {
        var (window, dialog, shown) = Open();

        Press(window, Key.Enter, PhysicalKey.Enter);

        Assert.Equal(FAContentDialogResult.None, Finish(window, dialog, shown));
    });

    [Fact]
    public void An_enter_key_held_when_the_dialog_opened_does_not_confirm() => session.Run(() =>
    {
        var held = AppDialogs.IsKeyHeld;
        AppDialogs.IsKeyHeld = key => key == Key.Enter;
        try
        {
            var (window, dialog, shown) = Open();
            Tab(window, dialog, "PrimaryButton");

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(FAContentDialogResult.None, Finish(window, dialog, shown));
        }
        finally
        {
            AppDialogs.IsKeyHeld = held;
        }
    });

    [Fact]
    public void An_enter_key_held_at_open_confirms_once_released_and_pressed_again() => session.Run(() =>
    {
        var held = AppDialogs.IsKeyHeld;
        AppDialogs.IsKeyHeld = key => key == Key.Enter;
        try
        {
            var (window, dialog, shown) = Open();
            Tab(window, dialog, "PrimaryButton");
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            Dispatcher.UIThread.RunJobs();
            Assert.False(shown.IsCompleted);

            Press(window, Key.Enter, PhysicalKey.Enter);

            Assert.Equal(FAContentDialogResult.Primary, Finish(window, dialog, shown));
        }
        finally
        {
            AppDialogs.IsKeyHeld = held;
        }
    });

    [Fact]
    public void An_alt_tap_does_not_confirm() => session.Run(() =>
    {
        var (window, dialog, shown) = Open();
        Tab(window, dialog, "PrimaryButton");

        window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(FAContentDialogResult.None, Finish(window, dialog, shown));
    });

    [Fact]
    public void A_programmatic_primary_close_returns_none() => session.Run(() =>
    {
        var (window, dialog, shown) = Open();

        dialog.Hide(FAContentDialogResult.Primary);
        AvaloniaSession.WaitFor(() => shown.IsCompleted);
        window.Close();

        Assert.Equal(FAContentDialogResult.None, shown.Result);
    });

    [Fact]
    public void A_dialog_shown_again_and_escaped_does_not_return_its_earlier_primary() => session.Run(() =>
    {
        var (window, dialog, shown) = Open();
        Click(window, Button(dialog, "PrimaryButton"));
        AvaloniaSession.WaitFor(() => shown.IsCompleted);
        Assert.Equal(FAContentDialogResult.Primary, shown.Result);

        var again = AppDialogs.ShowAsync(dialog, window);
        AvaloniaSession.WaitFor(() => dialog.IsLoaded);
        Press(window, Key.Escape, PhysicalKey.Escape);

        Assert.Equal(FAContentDialogResult.None, Finish(window, dialog, again));
    });

    [Fact]
    public void A_space_key_up_on_a_focused_secondary_button_does_not_choose_it() => session.Run(() =>
    {
        var (window, dialog, shown) = Open(secondary: "Minimize to tray");
        Button(dialog, "SecondaryButton").Focus();

        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(FAContentDialogResult.None, Finish(window, dialog, shown));
    });

    [Fact]
    public void A_click_on_the_primary_button_confirms() => session.Run(() =>
    {
        var (window, dialog, shown) = Open();

        Click(window, Button(dialog, "PrimaryButton"));

        Assert.Equal(FAContentDialogResult.Primary, Finish(window, dialog, shown));
    });

    [Fact]
    public void A_ui_automation_invoke_of_the_primary_button_confirms() => session.Run(() =>
    {
        var (window, dialog, shown) = Open();

        Invoke(Button(dialog, "PrimaryButton"));

        Assert.Equal(FAContentDialogResult.Primary, Finish(window, dialog, shown));
    });

    [Fact]
    public void A_ui_automation_invoke_of_the_secondary_button_chooses_it() => session.Run(() =>
    {
        var (window, dialog, shown) = Open(secondary: "Minimize to tray");

        Invoke(Button(dialog, "SecondaryButton"));

        Assert.Equal(FAContentDialogResult.Secondary, Finish(window, dialog, shown));
    });

    [Fact]
    public void A_click_raised_from_code_does_not_confirm() => session.Run(() =>
    {
        var (window, dialog, shown) = Open();

        Button(dialog, "PrimaryButton").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(FAContentDialogResult.None, Finish(window, dialog, shown));
    });

    [Fact]
    public void Enter_on_the_primary_button_after_tabbing_to_it_confirms() => session.Run(() =>
    {
        var (window, dialog, shown) = Open();
        Tab(window, dialog, "PrimaryButton");

        Press(window, Key.Enter, PhysicalKey.Enter);

        Assert.Equal(FAContentDialogResult.Primary, Finish(window, dialog, shown));
    });

    [Fact]
    public void Space_on_the_primary_button_after_tabbing_to_it_confirms() => session.Run(() =>
    {
        var (window, dialog, shown) = Open();
        Tab(window, dialog, "PrimaryButton");

        Press(window, Key.Space, PhysicalKey.Space);

        Assert.Equal(FAContentDialogResult.Primary, Finish(window, dialog, shown));
    });

    [Fact]
    public void A_dialog_opens_with_focus_on_its_close_button_when_it_has_no_default() => session.Run(() =>
    {
        var (window, dialog, shown) = Open();

        var focused = window.FocusManager!.GetFocusedElement();

        Finish(window, dialog, shown);
        Assert.Equal("CloseButton", (focused as Control)?.Name);
    });

    private static (Window Window, FAContentDialog Dialog, Task<FAContentDialogResult> Shown) Open(string? secondary = null)
    {
        var window = new Window { Width = 1280, Height = 820, Content = new Button { Content = "Behind" } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var dialog = new FAContentDialog
        {
            Title = "Install DBM?",
            Content = "DBM 12.1.14 on Forever",
            PrimaryButtonText = "Install",
            SecondaryButtonText = secondary ?? "",
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.None,
        };
        var shown = AppDialogs.ShowAsync(dialog, window);
        AvaloniaSession.WaitFor(() => dialog.IsLoaded);
        var settled = DateTime.UtcNow.AddMilliseconds(300);
        AvaloniaSession.WaitFor(() => DateTime.UtcNow > settled);
        return (window, dialog, shown);
    }

    private static FAContentDialogResult Finish(Window window, FAContentDialog dialog, Task<FAContentDialogResult> shown)
    {
        Dispatcher.UIThread.RunJobs();
        var deadline = DateTime.UtcNow.AddMilliseconds(500);
        while (!shown.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Yield();
        }

        if (!shown.IsCompleted)
        {
            AvaloniaSession.CloseDialog(dialog, shown);
        }

        window.Close();
        return shown.Result;
    }

    private static void Tab(Window window, FAContentDialog dialog, string name)
    {
        for (var i = 0; i < 4 && (window.FocusManager!.GetFocusedElement() as Control)?.Name != name; i++)
        {
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Same(Button(dialog, name), window.FocusManager!.GetFocusedElement());
    }

    private static void Press(Window window, Key key, PhysicalKey physical)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        window.KeyRelease(key, RawInputModifiers.None, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static Button Button(FAContentDialog dialog, string name) =>
        dialog.GetVisualDescendants().OfType<Button>().First(button => button.Name == name);

    private static void Invoke(Button button)
    {
        Assert.IsType<DialogCommandButton>(button);
        var peer = Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(button));
        peer.Invoke();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Window window, Control target)
    {
        Dispatcher.UIThread.RunJobs();
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
