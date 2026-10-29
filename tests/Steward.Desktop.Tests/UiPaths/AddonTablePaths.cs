using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using FluentAvalonia.UI.Controls;

using Steward.App.ViewModels;
using Steward.Desktop.Views;

using static Steward.Desktop.Tests.UiPaths.UiPathWorld;

namespace Steward.Desktop.Tests.UiPaths;

public sealed class AddonTablePaths(AvaloniaSession session)
{
    [Fact]
    public void P008_P009_tabs_and_filter() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "addons-filter");

        world.Step(8, "Updates tab reads No addons match", () =>
        {
            Click(world.Find<ListBoxItem>(item => AutomationProperties.GetName(item) is { } name && name.StartsWith("Updates", StringComparison.Ordinal)));
            Assert.Equal(1, world.Main.FilterIndex);
            Assert.NotNull(world.TryFind<TextBlock>(text => text.Text == "No addons match"));
            Click(world.Find<ListBoxItem>(item => item.Content as string == "All"));
            Assert.Equal(0, world.Main.FilterIndex);
        });

        world.Step(9, "filter bug shows BugSack and BugGrabber, then clears", () =>
        {
            Click(world.Find<TextBox>(box => AutomationProperties.GetName(box) == "Filter addons"));
            world.Type("bug");
            WaitFor(() => world.Main.TableRows.Count == 2);
            Assert.Equal(["BugSack", "BugGrabber"], world.Main.TableRows.Select(row => row.DisplayName).Order().Reverse());
            world.Shot("09", "filtered");
            for (var i = 0; i < 3; i++)
            {
                world.Press(Key.Back);
            }

            WaitFor(() => world.Main.TableRows.Count > 2);
        });
    });

    [Fact]
    public void P010_P014_row_menus() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "addons-menus");

        world.Step("212", "the first click on the row menu after a start opens it", () =>
        {
            var more = More(world, "BugSack");
            Click(more);
            Assert.True(more.Flyout!.IsOpen);
        });

        world.Step(10, "BugSack overflow menu items", () =>
            Assert.Equal(["Ignore updates", "Change channel", "Hide addon", "Open folder", "Reinstall", "Uninstall"], MenuHeaders(world)));

        world.Step(11, "menu item hover", () =>
        {
            var item = world.Find<MenuItem>(found => found.Header as string == "Change channel");
            Hover(item);
            Assert.True(IsOver(item));
        });

        world.Step(12, "Escape closes the menu", () =>
        {
            world.Press(Key.Escape);
            WaitFor(() => !More(world, "BugSack").Flyout!.IsOpen);
        });

        world.Step(13, "Steward overflow menu (auto-install)", () =>
        {
            Click(More(world, "Steward"));
            Assert.Equal(["Open folder", "Settings", "Reinstall"], MenuHeaders(world));
        });

        world.Step(14, "second click on the More button closes the menu", () =>
        {
            var more = More(world, "Steward");
            Click(more);
            Assert.False(more.Flyout!.IsOpen);
            Assert.Equal("More", ToolTip.GetTip(more));
        });
    });

    [Fact]
    public void P015_P019_P030_uninstall_confirmation() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "addons-uninstall");
        var bugSack = Path.Combine(world.BetaAddOns, "BugSack");

        world.Step(15, "Uninstall BugSack confirm dialog", () =>
        {
            OpenUninstall(world);
            var dialog = world.OpenDialog();
            Assert.Contains("Uninstall BugSack?", Text(dialog), StringComparison.Ordinal);
            Assert.Equal("CloseButton", (world.Focused as Control)?.Name);
        });

        world.Step(16, "Tab cycles inside the dialog", () =>
        {
            var visited = new List<string?>();
            for (var i = 0; i < 4; i++)
            {
                world.Press(Key.Tab);
                visited.Add((world.Focused as Control)?.Name);
                Assert.True(world.OpenDialog().IsVisualAncestorOf((Visual)world.Focused!));
            }

            Assert.Equal(["PrimaryButton", "CloseButton", "PrimaryButton", "CloseButton"], visited);
        });

        world.Step(17, "Escape closes the dialog", () =>
        {
            world.Press(Key.Escape);
            world.WaitForNoDialog();
            Assert.True(Directory.Exists(bugSack));
        });

        world.Step(18, "Cancel closes the dialog and removes nothing", () =>
        {
            OpenUninstall(world);
            Click(DialogButton(world.OpenDialog(), "CloseButton"));
            world.WaitForNoDialog();
            Assert.True(Directory.Exists(bugSack));
        });

        world.Step(19, "fresh dialog then Escape", () =>
        {
            OpenUninstall(world);
            world.Press(Key.Escape);
            world.WaitForNoDialog();
        });

        world.Step(30, "uninstall dialog re-show after other dialogs and Escape", () =>
        {
            Click(In(Row(world, "BugSack"), "Changelog"));
            world.WaitForDialog();
            world.Press(Key.Escape);
            world.WaitForNoDialog();
            OpenUninstall(world);
            Assert.Equal("CloseButton", (world.Focused as Control)?.Name);
            world.Press(Key.Escape);
            world.WaitForNoDialog();
            Assert.True(Directory.Exists(bugSack));
        });
    });

    [Fact]
    public void P021_P023_P028_P029_P031_release_channel_dialog() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "addons-channel");

        world.Step(21, "Release channel dialog from the row's channel pill", () =>
        {
            Click(In(Row(world, "BugSack"), "Release channel"));
            world.WaitForDialog<ReleaseChannelDialog>();
        });

        world.Step(22, "Pre-release selected, primary reads Use pre-release", () =>
        {
            Click(world.Find<RadioButton>(radio => AutomationProperties.GetName(radio) == "Pre-release"));
            Assert.Equal("Use pre-release", world.OpenDialog().PrimaryButtonText);
        });

        world.Step(23, "inline changelog in the channel dialog, Escape closes without saving", () =>
        {
            Click(world.Find<Button>(button => AutomationProperties.GetName(button) == "Changelog for v11.2.1-beta"));
            Assert.True(world.ByName<StackPanel>("ChangelogPanel").IsVisible);
            world.Shot("23", "changelog-open");
            world.Press(Key.Escape);
            world.WaitForNoDialog();
            Assert.Equal("release", world.Store.Load().Channels["bugsack"]);
        });

        world.Step(28, "channel dialog re-show and Escape", () =>
        {
            Click(In(Row(world, "BugSack"), "Release channel"));
            var dialog = world.WaitForDialog();
            Assert.Equal("Save", dialog.PrimaryButtonText);
            world.Press(Key.Escape);
            world.WaitForNoDialog();
        });

        world.Step(29, "channel dialog from the menu and Escape", () =>
        {
            Click(More(world, "BugSack"));
            Click(world.Find<MenuItem>(item => item.Header as string == "Change channel"));
            world.WaitForDialog<ReleaseChannelDialog>();
            world.Press(Key.Escape);
            world.WaitForNoDialog();
        });

        world.Step(31, "channel dialog Tab order", () =>
        {
            Click(In(Row(world, "BugSack"), "Release channel"));
            var dialog = world.WaitForDialog();
            var visited = new List<string>();
            for (var i = 0; i < 8; i++)
            {
                world.Press(Key.Tab);
                var focused = (Control)world.Focused!;
                Assert.True(dialog.IsVisualAncestorOf(focused), $"Tab left the dialog for {focused}");
                visited.Add(focused.Name ?? AutomationProperties.GetName(focused) ?? focused.GetType().Name);
            }

            Assert.Contains("PrimaryButton", visited);
            Assert.Contains("CloseButton", visited);
            Assert.Contains("Pre-release", visited);
            world.Press(Key.Escape);
            world.WaitForNoDialog();
        });
    });

    [Fact]
    public void P024_P027_changelogs() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "addons-changelog");

        world.Step(24, "Steward changelog dialog with sub-addon sections", () =>
        {
            Click(In(Row(world, "Steward"), "Changelog"));
            var dialog = world.WaitForDialog<ChangelogDialog>();
            var text = Text(dialog);
            Assert.Contains("Fixed the roster export on pre-release", text, StringComparison.Ordinal);
            Assert.Contains("Steward: Guides", text, StringComparison.Ordinal);
        });

        world.Step(25, "Enter closes the changelog", () =>
        {
            world.Press(Key.Enter);
            world.WaitForNoDialog();
        });

        world.Step(26, "Escape closes the BugSack changelog", () =>
        {
            Click(In(Row(world, "BugSack"), "Changelog"));
            Assert.Contains("v11.2.0", Text(world.WaitForDialog()), StringComparison.Ordinal);
            world.Press(Key.Escape);
            world.WaitForNoDialog();
        });

        world.Step(27, "BugGrabber changelog, Escape closes", () =>
        {
            Click(In(Row(world, "BugGrabber"), "Changelog"));
            Assert.Contains("BugGrabber", Text(world.WaitForDialog()), StringComparison.Ordinal);
            world.Press(Key.Escape);
            world.WaitForNoDialog();
        });
    });

    [Fact]
    public void P032_P035_folders_dialog() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "addons-folders");

        world.Step(32, "Steward folders dialog with the Released tooltip", () =>
        {
            OpenFolders(world);
            var released = Released(world, "Steward: Guides");
            Hover(released);
            Assert.True(ToolTip.GetIsOpen(released));
            Assert.Contains("2026", Tip(released), StringComparison.Ordinal);
        });

        world.Step(33, "a click through the Released tooltip opens the changelog", () =>
        {
            Assert.All(world.Window.OpenedPopups.Where(popup => popup.Child is ToolTip), popup => Assert.False(popup.IsHitTestVisible));
            Click(world.Find<Button>(button => AutomationProperties.GetName(button) == "Changelog for Steward: Guides"));
            world.WaitForDialog<ChangelogDialog>();
        });

        world.Step(34, "folders dialog re-shown after the changelog keeps Tab inside", () =>
        {
            world.Press(Key.Escape);
            var dialog = WaitForFolders(world);
            for (var i = 0; i < 6; i++)
            {
                world.Press(Key.Tab);
                Assert.True(dialog.IsVisualAncestorOf((Visual)world.Focused!), $"Tab left the dialog for {world.Focused}");
            }

            Click(world.Find<Button>(button => AutomationProperties.GetName(button) == "Changelog for Steward: Journey"));
            world.WaitForDialog<ChangelogDialog>();
            world.Press(Key.Escape);
            WaitForFolders(world);
        });

        world.Step(35, "Escape closes the re-shown folders dialog", () =>
        {
            world.Press(Key.Escape);
            world.WaitForNoDialog();
        });

        world.Step("X2", "folders dialog re-shown five times, each through the Released tooltip", () =>
        {
            OpenFolders(world);
            for (var i = 0; i < 5; i++)
            {
                var released = Released(world, "Steward: Guides");
                Hover(released);
                Assert.True(ToolTip.GetIsOpen(released));
                Click(world.Find<Button>(button => AutomationProperties.GetName(button) == "Changelog for Steward: Guides"));
                world.WaitForDialog<ChangelogDialog>();
                world.Press(Key.Escape);
                WaitForFolders(world);
            }

            world.Press(Key.Escape);
            world.WaitForNoDialog();
        });

        world.Step("X4", "tooltip-then-click three times in the table", () =>
        {
            for (var i = 0; i < 3; i++)
            {
                var row = Row(world, "BugSack");
                Hover(row.GetVisualDescendants().OfType<TextBlock>().First(text => text.Name == "ReleasedLine"));
                Click(In(row, "Changelog"));
                world.WaitForDialog<ChangelogDialog>();
                world.Press(Key.Escape);
                world.WaitForNoDialog();
            }
        });
    });

    [Fact]
    public void P088_P099_hover_and_tooltips() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "addons-tooltips");

        world.Step(88, "table row has no hover fill", () =>
        {
            var row = Row(world, "BugSack");
            var rest = row.Background;
            Hover(row);
            Assert.Equal(rest, row.Background);
        });

        world.Step(93, "version tooltip", () =>
        {
            var version = In(Row(world, "Steward"), "Changelog");
            Hover(version);
            Assert.Contains("Changelog", Tip(version), StringComparison.Ordinal);
            Assert.True(ToolTip.GetIsOpen(version));
        });

        world.Step(94, "released tooltip with a date", () =>
        {
            var released = Row(world, "BugSack").GetVisualDescendants().OfType<TextBlock>().First(text => text.Name == "ReleasedLine");
            Hover(released);
            Assert.Contains("2026", Tip(released), StringComparison.Ordinal);
        });

        world.Step(95, "single-channel tooltip", () =>
        {
            var channel = Row(world, "Steward").GetVisualDescendants().OfType<ContentControl>().First(found => found.IsEffectivelyVisible && ToolTip.GetTip(found) is string tip && tip.StartsWith("Only", StringComparison.Ordinal));
            Hover(channel);
            Assert.Equal("Only pre-release builds are published for Steward.", Tip(channel));
        });

        world.Step(96, "status tooltip Up to date", () =>
        {
            var status = Row(world, "BugSack").GetVisualDescendants().OfType<TextBlock>().First(text => text.IsEffectivelyVisible && text.Text == "Up to date");
            Hover(status);
            Assert.Equal("Up to date", Tip(status));
        });

        world.Step(97, "lock notice tooltip", () =>
        {
            var notice = In(Row(world, "HoobiScripts"), "Protected addon, issued to your Steward account. Do not share it.");
            Hover(notice);
            Assert.Equal("Protected addon, issued to your Steward account. Do not share it.", Tip(notice));
        });

        world.Step(98, "More tooltip", () =>
        {
            var more = More(world, "BugSack");
            Hover(more);
            Assert.Equal("More", Tip(more));
        });

        world.Step(90, "refresh hover with its tooltip", () =>
        {
            var refresh = world.Named("Check for updates");
            Hover(refresh);
            Assert.True(IsOver(refresh));
        });

        world.Step(99, "refresh tooltip", () => Assert.False(string.IsNullOrEmpty(Tip(world.Named("Check for updates")))));
    });

    internal static Border Row(UiPathWorld world, string name) =>
        world.Find<Border>(border => border.Classes.Contains("TableRow") && (border.DataContext as IAddonTableRow)?.DisplayName == name);

    internal static Button In(Visual root, string automationName) =>
        root.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == automationName);

    internal static Button More(UiPathWorld world, string name) => In(Row(world, name), "More options");

    internal static List<string> MenuHeaders(UiPathWorld world) =>
        [.. world.Everything.OfType<MenuItem>().Where(item => item.IsEffectivelyVisible).Select(item => item.Header as string ?? "")];

    private static void OpenUninstall(UiPathWorld world)
    {
        Click(More(world, "BugSack"));
        Click(world.Find<MenuItem>(item => item.Header as string == "Uninstall"));
        world.WaitForDialog();
    }

    private static void OpenFolders(UiPathWorld world)
    {
        Click(In(Row(world, "Steward"), "Show the other installed folders"));
        WaitForFolders(world);
    }

    private static AddonFoldersDialog WaitForFolders(UiPathWorld world)
    {
        var dialog = world.WaitForDialog<AddonFoldersDialog>();
        WaitFor(() => dialog.RowsLoaded.IsCompleted);
        Pump();
        return dialog;
    }

    private static TextBlock Released(UiPathWorld world, string addon)
    {
        var dialog = world.Find<AddonFoldersDialog>(_ => true);
        var row = dialog.GetVisualDescendants().OfType<Grid>().First(grid => grid.Children.OfType<Grid>().Any(title => Text(title) == addon));
        return row.GetVisualDescendants().OfType<TextBlock>().First(text => ToolTip.GetTip(text) is string tip && tip.Length > 0 && text.Text?.StartsWith("Released", StringComparison.Ordinal) == true);
    }
}
