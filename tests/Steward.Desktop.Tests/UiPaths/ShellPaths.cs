using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;

using Steward.App;
using Steward.Desktop.Views;

using static Steward.Desktop.Tests.UiPaths.UiPathWorld;

namespace Steward.Desktop.Tests.UiPaths;

public sealed class ShellPaths(AvaloniaSession session)
{
    private static readonly string[] AccountLines = ["Hoobi", "Global admin", "Forever Guild", "Open guild panel", "Sign out"];

    [Fact]
    public void P000_P001_P141_start_signed_in_and_again() => session.Run(() =>
    {
        var world = new UiPathWorld(session, "shell");
        using (world)
        {
            world.Step(0, "start signed in", () =>
            {
                Assert.Equal(AppInfo.WindowTitle, world.Window.Title);
                Assert.True(world.Main.IsSignedIn);
                Assert.True(world.Main.IsAuthorized);
                Assert.IsType<HomePage>(world.Window.CurrentPage);
                Assert.Contains(world.Main.TableRows, row => row.DisplayName == "BugSack");
            });

            world.Step(1, "default and 1280x820 window size", () =>
            {
                Assert.Equal(new Size(980, 720), world.DefaultSize);
                Assert.Equal(new Size(1280, 820), world.Window.ClientSize);
                world.Window.Width = 980;
                world.Window.Height = 720;
                Pump();
                world.Shot("01", "980x720");
                world.Window.Width = 1280;
                world.Window.Height = 820;
            });
        }

        using var again = new UiPathWorld(session, "shell", restartOf: world);
        again.Step(141, "a normal launch signs in again", () =>
        {
            Assert.True(again.Main.IsAuthorized);
            Assert.Contains(again.Api.Requests, request => request.EndsWith("/api/me", StringComparison.Ordinal));
            Assert.NotNull(again.Store.Load().AddonCatalogue);
            Assert.Contains(again.Main.TableRows, row => row.DisplayName == "BugSack");
        });
    });

    [Fact]
    public void P073_P078_P091_P104_close_and_minimise() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "window");
        var tray = true;

        if (OperatingSystem.IsMacOS())
        {
            world.Step(78, "a minimise hides to the tray", () => MinimiseHides(world));
            UiPathLedger.Record("73", "skip: macOS hides the window on close by design, with no confirmation");
            UiPathLedger.Record("74", "skip: macOS hides the window on close by design, with no confirmation");
            UiPathLedger.Record("104", "skip: a tray is always available on macOS");
            world.Window.Close();
            WaitFor(() => !world.Window.IsVisible);
            world.Window.ShowFromTray();
        }
        else
        {
            world.Step(73, "close confirmation", () =>
            {
                world.Window.Close();
                var dialog = world.WaitForDialog();
                Assert.Contains("Are you sure you want to close?", Text(dialog), StringComparison.Ordinal);
                Assert.Equal("Close anyway", dialog.PrimaryButtonText);
                Assert.Equal("SecondaryButton", (world.Focused as Control)?.Name);
                tray = dialog.SecondaryButtonText == "Minimize to tray";
                Assert.True(tray || dialog.SecondaryButtonText == "Minimize");
            });

            if (tray)
            {
                UiPathLedger.Record("104", "skip: a tray host is running here, so the confirmation offers Minimize to tray");
            }
            else
            {
                world.Step(104, "close confirmation offers Minimize without a tray", () => Assert.Equal("Minimize", world.OpenDialog().SecondaryButtonText));
            }

            world.Step(74, "Minimize to tray hides the window", () =>
            {
                Click(DialogButton(world.OpenDialog(), "SecondaryButton"));
                world.WaitForNoDialog();
                WaitFor(() => tray ? !world.Window.IsVisible : world.Window.WindowState == WindowState.Minimized);
                world.Window.ShowFromTray();
                WaitFor(() => world.Window.IsVisible && world.Window.WindowState == WindowState.Normal);
            });

            world.Step(78, "a minimise hides to the tray", () => MinimiseHides(world, tray));
        }

        world.Step(91, "row menu item hover after a tray restore", () =>
        {
            Click(AddonTablePaths.More(world, "BugSack"));
            var item = world.Find<MenuItem>(found => found.Header as string == "Open folder");
            Hover(item);
            Assert.True(IsOver(item));
            world.Press(Key.Escape);
        });
    });

    [Fact]
    public void P080_P087_account_flyout_and_hover() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "account");
        var account = world.ByName<Button>("AccountButton");

        world.Step(80, "account flyout", () =>
        {
            Click(account);
            Assert.True(account.Flyout!.IsOpen);
            var text = Text(world.Find<StackPanel>(panel => panel.Width == 280));
            foreach (var expected in AccountLines)
            {
                Assert.Contains(expected, text, StringComparison.Ordinal);
            }
        });

        world.Step(81, "one guild: the guild row is not interactive", () =>
        {
            var guild = world.ByName<Button>("GuildRow");
            Assert.False(guild.IsHitTestVisible);
            Assert.False(guild.Focusable);
        });

        world.Step(82, "account menu item hover", () =>
        {
            var panel = world.ByName<Button>("OpenGuildPanelButton");
            Hover(panel);
            Assert.True(IsOver(panel));
        });

        world.Step(83, "Escape closes the account flyout", () =>
        {
            world.Press(Key.Escape);
            Assert.False(account.Flyout!.IsOpen);
        });

        world.Step(84, "second click closes the account flyout, the tooltip leads with the name", () =>
        {
            Click(account);
            Assert.True(account.Flyout!.IsOpen);
            Click(account);
            Assert.False(account.Flyout.IsOpen);
            Assert.StartsWith("Hoobi", Tip(account), StringComparison.Ordinal);
        });

        world.Step(85, "nav item hover and tooltip", () =>
        {
            var sync = world.ByName<Button>("SyncItem");
            Hover(sync);
            Assert.True(IsOver(sync));
            Assert.True(ToolTip.GetIsOpen(sync));
            Assert.Equal("Sync", Tip(sync));
        });

        world.Step(87, "segmented tab hover", () =>
        {
            var updates = world.Find<ListBoxItem>(item => AutomationProperties.GetName(item) is { } name && name.StartsWith("Updates", StringComparison.Ordinal));
            Hover(updates);
            Assert.True(IsOver(updates));
            Assert.Equal(0, world.Main.FilterIndex);
        });
    });

    private static void MinimiseHides(UiPathWorld world, bool tray = true)
    {
        world.Window.WindowState = WindowState.Minimized;
        WaitFor(() => tray ? !world.Window.IsVisible : world.Window.WindowState == WindowState.Minimized);
        world.Window.ShowFromTray();
        WaitFor(() => world.Window.IsVisible && world.Window.WindowState == WindowState.Normal);
    }
}
