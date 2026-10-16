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

public sealed class DialogModalityTests(AvaloniaSession session)
{
    [Fact]
    public void Folders_dialog_closes_from_its_close_button_after_handing_over_to_a_changelog_and_blocks_the_window_behind() => session.Run(() =>
    {
        using var host = session.NewHost();
        var addOns = session.NewFolder();
        Directory.CreateDirectory(Path.Combine(addOns, "StewardGuides"));
        File.WriteAllText(Path.Combine(addOns, "StewardGuides", "StewardGuides.toc"), "## Interface: 16001\n## Title: Steward: Guides\n## Version: 1.0.0\n");
        var changelog = new[] { new ChangelogBlock("item", 0, 0, [new ChangelogRun("Fixed a thing")]) };
        var folders = new[] { new FolderEntry(addOns, "StewardGuides", null, "Steward: Guides", changelog, null, "pre-release") };
        var dialog = new AddonFoldersDialog("Steward", folders, host.Services.GetRequiredService<AvaloniaPlatform>(), NullLogger.Instance);
        var (window, behindClicks) = WindowWithButtonBehind();

        var shown = AppDialogs.ShowAsync(dialog, window);
        AvaloniaSession.WaitFor(() => dialog.RowsLoaded.IsCompleted && dialog.IsLoaded);
        Click(window, dialog.GetVisualDescendants().OfType<Button>().Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Changelog for Steward: Guides"));
        AvaloniaSession.WaitFor(() => shown.IsCompleted);

        var changelogDialog = new ChangelogDialog("Steward: Guides", changelog);
        var changelogShown = AppDialogs.ShowAsync(changelogDialog, window);
        AvaloniaSession.WaitFor(() => changelogDialog.IsLoaded);
        Click(window, CloseButton(changelogDialog));
        AvaloniaSession.WaitFor(() => changelogShown.IsCompleted);

        var reshown = AppDialogs.ShowAsync(dialog, window);
        AvaloniaSession.WaitFor(() => dialog.IsLoaded);
        ClickAt(window, new Point(20, 20));
        var blocked = behindClicks();
        Click(window, CloseButton(dialog));
        AvaloniaSession.WaitFor(() => reshown.IsCompleted);
        window.Close();

        Assert.Equal(0, blocked);
        Assert.Equal(FAContentDialogResult.None, reshown.Result);
    });

    [Fact]
    public void A_dialog_covers_the_whole_shell_title_bar_included_until_it_closes() => session.Run(() =>
    {
        using var host = session.NewHost();
        var window = host.Services.GetRequiredService<MainWindow>();
        window.Width = 1280;
        window.Height = 820;
        window.Show();
        var dialog = new FAContentDialog { Title = "Title", Content = "Body", CloseButtonText = "Close" };

        var shown = AppDialogs.ShowAsync(dialog, window);
        AvaloniaSession.WaitFor(() => dialog.IsLoaded);
        Dispatcher.UIThread.RunJobs();
        var covered = new[] { new Point(640, 24), new Point(24, 400), new Point(1200, 780) }
            .All(point => window.InputHitTest(point) is Visual hit && hit.GetSelfAndVisualAncestors().Contains(dialog));
        Click(window, CloseButton(dialog));
        AvaloniaSession.WaitFor(() => shown.IsCompleted);

        Assert.True(covered);
    });

    private static (Window Window, Func<int> Clicks) WindowWithButtonBehind()
    {
        var clicks = 0;
        var behind = new Button { Content = "Behind", Width = 100, Height = 40, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        behind.Click += (_, _) => clicks++;
        var window = new Window { Width = 1280, Height = 820, Content = behind };
        window.Show();
        return (window, () => clicks);
    }

    private static Button CloseButton(FAContentDialog dialog) =>
        dialog.GetVisualDescendants().OfType<Button>().First(button => button.Name == "CloseButton");

    private static void Click(Window window, Control target)
    {
        Dispatcher.UIThread.RunJobs();
        ClickAt(window, target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value);
    }

    private static void ClickAt(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
