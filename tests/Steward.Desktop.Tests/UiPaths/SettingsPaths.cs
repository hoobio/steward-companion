using Avalonia;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using Steward.Desktop.Views;

using static Steward.Desktop.Tests.UiPaths.UiPathWorld;

namespace Steward.Desktop.Tests.UiPaths;

public sealed class SettingsPaths(AvaloniaSession session)
{
    [Fact]
    public void P056_P062_P072_P103_settings_page() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "settings");
        ScrollViewer Scroller() => world.ByName<ScrollViewer>("Scroller");

        world.Step(57, "Settings from the nav with its tooltip", () =>
        {
            var item = world.ByName<Button>("SettingsItem");
            Hover(item);
            Assert.True(ToolTip.GetIsOpen(item));
            Assert.Equal("Settings", Tip(item));
            Click(item);
            Assert.IsType<SettingsPage>(world.Window.CurrentPage);
        });

        world.Step(56, "Settings top", () =>
        {
            var text = Text(world.Window.CurrentPage!);
            Assert.Contains("Installs", text, StringComparison.Ordinal);
            Assert.Contains("Behaviour", text, StringComparison.Ordinal);
            Assert.Contains("Only available in a released build", text, StringComparison.Ordinal);
            Assert.Equal(0, Scroller().Offset.Y);
        });

        world.Step(103, "tray cards follow the tray capability", () =>
        {
            var minimise = world.ByName<SettingsCard>("MinimizeToTrayCard");
            var noTray = minimise.Description as string == "Not available: no system tray is running on this desktop";
            Assert.Equal(!noTray, minimise.IsEnabled);
            if (!OperatingSystem.IsLinux())
            {
                Assert.True(minimise.IsEnabled);
            }
        });

        world.Step(61, "segmented control hover", () =>
        {
            var always = world.Find<ListBoxItem>(item => item.Content as string == "Always");
            Hover(always);
            Assert.True(IsOver(always));
            Assert.Equal("never", world.Store.Load().AutoUpdate);
        });

        world.Step(62, "one wheel notch scrolls Settings", () =>
        {
            Wheel(world.Window.CurrentPage!.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == "Behaviour"), -1);
            Settle(Scroller());
            Assert.True(Scroller().Offset.Y > 0);
        });

        world.Step("152", "three wheel notches add up to three notch distances", () =>
        {
            var notch = Scroller().Offset.Y;
            var (root, point) = Locate(world.Window.CurrentPage!.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == "Behaviour"));
            for (var i = 0; i < 3; i++)
            {
                root.MouseWheel(point, new Vector(0, -1));
            }

            Settle(Scroller());
            Assert.Equal(Math.Min(4 * notch, Scroller().Extent.Height - Scroller().Viewport.Height), Scroller().Offset.Y, 1);
        });

        world.Step(58, "Settings bottom, About card", () =>
        {
            for (var notch = 0; notch < 12; notch++)
            {
                Wheel(Scroller(), -3);
            }

            Settle(Scroller());
            Assert.Equal(Scroller().Extent.Height - Scroller().Viewport.Height, Scroller().Offset.Y, 1);
            var about = world.ByName<SettingsCard>("AboutCard");
            Assert.Contains("Debug build, not updated automatically", about.Description as string, StringComparison.Ordinal);
        });

        world.Step(72, "Check for updates", () =>
        {
            Click(world.ByName<SettingsCard>("AboutCard").GetVisualDescendants().OfType<Button>().First(button => button.Content as string == "Check for updates"));
            WaitFor(() => !world.Main.IsCheckingAppUpdate);
            Assert.Null(world.Main.AppUpdate);
            Assert.Empty(world.Opened);
        });
    });

    [Fact]
    public void P063_P066_curseforge_off_and_on() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "settings-curseforge");
        ToggleSwitch CurseForge() => world.Find<ToggleSwitch>(toggle => AutomationProperties.GetName(toggle) == "Enable CurseForge addon management");
        string[] curseForgeRows = ["BugSack", "BugGrabber"];

        world.Step(63, "CurseForge off persists to state", () =>
        {
            world.Navigate("Settings");
            Click(CurseForge());
            Assert.False(world.Store.Load().CurseForgeEnabled);
        });

        world.Step(64, "Addons with CurseForge off hides its rows and Get addons", () =>
        {
            world.Navigate("Addons");
            WaitFor(() => !world.Main.TableRows.Any(row => curseForgeRows.Contains(row.DisplayName)));
            Assert.DoesNotContain(world.Main.TableRows, row => row.DisplayName is "Details! Damage Meter" or "WeakAuras");
            Assert.Null(world.TryFind<Button>(button => AutomationProperties.GetName(button) == "Get addons"));
            Assert.Equal("4", world.Main.HiddenCountText);
        });

        world.Step(65, "Hidden segment lists the hidden rows", () =>
        {
            Click(world.Find<ListBoxItem>(item => AutomationProperties.GetName(item) is { } name && name.StartsWith("Hidden", StringComparison.Ordinal)));
            WaitFor(() => world.Main.TableRows.Count == 4);
            Assert.Contains(world.Main.TableRows, row => row.DisplayName == "BugSack");
            Assert.NotNull(world.TryFind<TextBlock>(text => text.Text == "Hidden" && text.FindAncestorOfType<Border>()?.Classes.Contains("TableRow") != true && text.FontSize < 13));
        });

        world.Step(66, "CurseForge on again restores the rows", () =>
        {
            Click(world.Find<ListBoxItem>(item => item.Content as string == "All"));
            world.Navigate("Settings");
            Click(CurseForge());
            Assert.True(world.Store.Load().CurseForgeEnabled);
            world.Navigate("Addons");
            WaitFor(() => curseForgeRows.All(name => world.Main.TableRows.Any(row => row.DisplayName == name)));
            Assert.NotNull(world.TryFind<Button>(button => AutomationProperties.GetName(button) == "Get addons"));
        });
    });

    [Fact]
    public void P067_P071_P133_P134_installs_and_edit_install() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "settings-installs");

        world.Step(67, "Installs card collapses", () =>
        {
            world.Navigate("Settings");
            Assert.True(world.ByName<Control>("InstallsList").IsEffectivelyVisible);
            Click(world.ByName<SettingsCard>("InstallsHeader"));
            Assert.Null(world.TryFind<Control>(control => control.Name == "InstallsList"));
        });

        world.Step(68, "Installs card reopens", () =>
        {
            Click(world.ByName<SettingsCard>("InstallsHeader"));
            Assert.True(world.ByName<Control>("InstallsList").IsEffectivelyVisible);
        });

        world.Step(69, "Edit install dialog focuses the name box", () =>
        {
            Click(world.Find<Button>(button => button.Content as string == "Edit" && button.FindAncestorOfType<SettingsPage>() is not null));
            world.WaitForDialog<EditInstallDialog>();
            Assert.Equal("NameBox", (world.Focused as Control)?.Name);
        });

        world.Step(70, "Edit install Tab order", () =>
        {
            var dialog = world.OpenDialog();
            var visited = new List<string>();
            for (var i = 0; i < 5; i++)
            {
                world.Press(Key.Tab);
                var focused = (Control)world.Focused!;
                Assert.True(dialog.IsVisualAncestorOf(focused), $"Tab left the dialog for {focused}");
                visited.Add(focused.Name ?? (focused as ContentControl)?.Content as string ?? focused.GetType().Name);
            }

            Assert.Equal(["GameVersionBox", "Change", "PrimaryButton", "CloseButton", "NameBox"], visited);
        });

        world.Step(71, "Escape closes Edit install, nothing saved", () =>
        {
            world.Type("Renamed");
            world.Press(Key.Escape);
            world.WaitForNoDialog();
            Assert.Empty(world.Store.Load().InstallLabels);
        });

        world.Step(133, "Edit install game version list", () =>
        {
            Click(world.Find<Button>(button => button.Content as string == "Edit" && button.FindAncestorOfType<SettingsPage>() is not null));
            world.WaitForDialog<EditInstallDialog>();
            var versions = world.ByName<ComboBox>("GameVersionBox");
            Click(versions);
            Assert.True(versions.IsDropDownOpen);
            var labels = world.Everything.OfType<ComboBoxItem>().Where(item => item.IsEffectivelyVisible).Select(item => Text(item)).ToList();
            Assert.Contains(labels, label => label.Contains("Forever - Beta", StringComparison.Ordinal));
            Assert.Contains(labels, label => label.Contains("World of Warcraft: Forever", StringComparison.Ordinal) || label == "Forever");
        });

        world.Step(134, "Escape twice closes the list and the dialog", () =>
        {
            var versions = world.ByName<ComboBox>("GameVersionBox");
            world.Press(Key.Escape);
            Assert.False(versions.IsDropDownOpen);
            Assert.NotNull(world.TryFind<EditInstallDialog>(_ => true));
            world.Press(Key.Escape);
            world.WaitForNoDialog();
        });
    });

    private static void Settle(ScrollViewer scroller)
    {
        var last = scroller.Offset.Y;
        var since = DateTime.UtcNow;
        WaitFor(() =>
        {
            if (scroller.Offset.Y != last)
            {
                last = scroller.Offset.Y;
                since = DateTime.UtcNow;
                return false;
            }

            return DateTime.UtcNow - since > TimeSpan.FromMilliseconds(300);
        });
    }
}
