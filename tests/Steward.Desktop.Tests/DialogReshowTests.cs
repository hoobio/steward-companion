using System.Diagnostics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Steward.App.ViewModels;
using Steward.Core;
using Steward.Desktop.Services;
using Steward.Desktop.Views;

namespace Steward.Desktop.Tests;

public sealed class DialogReshowTests(AvaloniaSession session)
{
    private static readonly ChangelogBlock[] Changelog = [new ChangelogBlock("item", 0, 0, [new ChangelogRun("Fixed a thing")])];

    [Fact]
    public void A_dialog_shown_again_after_a_handover_closes_on_escape() => session.Run(() =>
    {
        using var host = session.NewHost();
        var (window, dialog, reshown) = HandOverAndShowAgain(host.Services);

        Press(window, Key.Escape, PhysicalKey.Escape);
        var closed = Settle(() => reshown.IsCompleted);
        Finish(window, dialog, reshown);

        Assert.True(closed);
        Assert.Null(dialog.ChangelogRequest);
    });

    [Fact]
    public void A_dialog_shown_again_after_a_handover_keeps_tab_inside() => session.Run(() =>
    {
        using var host = session.NewHost();
        var (window, dialog, reshown) = HandOverAndShowAgain(host.Services);

        Press(window, Key.Tab, PhysicalKey.Tab);
        var focused = window.FocusManager!.GetFocusedElement() as Visual;
        Finish(window, dialog, reshown);

        Assert.True(focused is not null && dialog.IsVisualAncestorOf(focused), $"focus left the dialog for {focused}");
    });

    [Fact]
    public void A_dialog_shown_again_after_a_handover_opens_a_changelog_on_one_click() => session.Run(() =>
    {
        using var host = session.NewHost();
        var (window, dialog, reshown) = HandOverAndShowAgain(host.Services);

        Hover(window, ChangelogButton(dialog, "Steward: Auctioning"), 500);
        Click(window, ChangelogButton(dialog, "Steward: Auctioning"));
        Settle(() => reshown.IsCompleted);
        Finish(window, dialog, reshown);

        Assert.Equal("StewardAuctioning", dialog.ChangelogRequest?.Folder);
    });

    private (Window Window, AddonFoldersDialog Dialog, Task<FAContentDialogResult> Reshown) HandOverAndShowAgain(IServiceProvider services)
    {
        var addOns = session.NewFolder();
        var folders = new[] { ("StewardGuides", "Steward: Guides"), ("StewardAuctioning", "Steward: Auctioning") }.Select(addon =>
        {
            Directory.CreateDirectory(Path.Combine(addOns, addon.Item1));
            File.WriteAllText(Path.Combine(addOns, addon.Item1, addon.Item1 + ".toc"), $"## Interface: 16001\n## Title: {addon.Item2}\n## Version: 1.0.0\n");
            return new FolderEntry(addOns, addon.Item1, null, addon.Item2, Changelog, null, "pre-release");
        }).ToArray();
        var dialog = new AddonFoldersDialog("Steward", folders, services.GetRequiredService<AvaloniaPlatform>(), NullLogger.Instance);
        var behind = new Button { Content = "Behind", Width = 100, Height = 40, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 1280, Height = 820, Content = behind };
        window.Show();
        Click(window, behind);

        var shown = AppDialogs.ShowAsync(dialog, window);
        AvaloniaSession.WaitFor(() => dialog.RowsLoaded.IsCompleted && dialog.IsLoaded);
        Hover(window, ChangelogButton(dialog, "Steward: Guides"), 500);
        Click(window, ChangelogButton(dialog, "Steward: Guides"));
        AvaloniaSession.WaitFor(() => shown.IsCompleted);
        dialog.ClearChangelogRequest();

        var changelogDialog = new ChangelogDialog("Steward: Guides", Changelog);
        var changelogShown = AppDialogs.ShowAsync(changelogDialog, window);
        AvaloniaSession.WaitFor(() => changelogDialog.IsLoaded);
        Hover(window, CloseButton(changelogDialog), 500);
        Click(window, CloseButton(changelogDialog));
        AvaloniaSession.WaitFor(() => changelogShown.IsCompleted);

        var reshown = AppDialogs.ShowAsync(dialog, window);
        AvaloniaSession.WaitFor(() => dialog.IsLoaded);
        Hover(window, ChangelogButton(dialog, "Steward: Guides"), 200);
        return (window, dialog, reshown);
    }

    private static void Hover(Window window, Control target, int milliseconds)
    {
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value);
        var hovered = Stopwatch.StartNew();
        AvaloniaSession.WaitFor(() => hovered.ElapsedMilliseconds > milliseconds);
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    private static bool Settle(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Yield();
        }

        return condition();
    }

    private static void Finish(Window window, FAContentDialog dialog, Task reshown)
    {
        if (!reshown.IsCompleted)
        {
            AvaloniaSession.CloseDialog(dialog, reshown);
        }

        window.Close();
    }

    private static void Press(Window window, Key key, PhysicalKey physical)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        window.KeyRelease(key, RawInputModifiers.None, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static Button ChangelogButton(FAContentDialog dialog, string addon) =>
        dialog.GetVisualDescendants().OfType<Button>().Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == $"Changelog for {addon}");

    private static Button CloseButton(FAContentDialog dialog) =>
        dialog.GetVisualDescendants().OfType<Button>().First(button => button.Name == "CloseButton");

    private static void Click(Window window, Control target)
    {
        Dispatcher.UIThread.RunJobs();
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
