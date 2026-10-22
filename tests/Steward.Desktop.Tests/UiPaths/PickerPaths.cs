using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;

using FluentAvalonia.UI.Controls;

using Steward.Desktop.Views;

using static Steward.Desktop.Tests.UiPaths.UiPathWorld;

namespace Steward.Desktop.Tests.UiPaths;

public sealed class PickerPaths(AvaloniaSession session)
{
    [Fact]
    public void P042_P048_install_picker() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "install-picker");
        var picker = world.ByName<Button>("InstallPicker");

        world.Step(42, "install picker flyout", () =>
        {
            Click(picker);
            Assert.True(picker.Flyout!.IsOpen);
            Assert.NotNull(world.TryFind<Button>(button => button.Classes.Contains("Menu") && AutomationProperties.GetName(button) == "Forever - Beta"));
        });

        world.Step(43, "picker row hover with the path tooltip", () =>
        {
            var row = world.Find<Button>(button => button.Classes.Contains("Menu") && AutomationProperties.GetName(button) == "Forever - Beta");
            Hover(row);
            Assert.Equal(world.Beta, Tip(row));
            Assert.True(ToolTip.GetIsOpen(row));
        });

        world.Step(92, "picker row hover", () =>
            Assert.True(IsOver(world.Find<Button>(button => button.Classes.Contains("Menu") && AutomationProperties.GetName(button) == "Forever - Beta"))));

        world.Step(44, "pencil hover Edit install tooltip and menu item hover", () =>
        {
            var pencil = world.Find<Button>(button => ToolTip.GetTip(button) as string == "Edit install");
            Hover(pencil);
            Assert.True(ToolTip.GetIsOpen(pencil));
            var open = MenuButton(world, "Open AddOns folder");
            Hover(open);
            Assert.True(IsOver(open));
        });

        world.Step(45, "second click on the picker closes the flyout", () =>
        {
            Click(picker);
            Assert.False(picker.Flyout!.IsOpen);
        });

        world.Step(46, "pencil opens Edit install on Settings, Escape closes", () =>
        {
            Click(picker);
            Click(world.Find<Button>(button => ToolTip.GetTip(button) as string == "Edit install"));
            world.WaitForDialog<EditInstallDialog>();
            Assert.IsType<SettingsPage>(world.Window.CurrentPage);
            world.Press(Key.Escape);
            world.WaitForNoDialog();
        });

        world.Step(47, "picker reopens, an outside click closes it", () =>
        {
            world.Navigate("Addons");
            Click(picker);
            Assert.True(picker.Flyout!.IsOpen);
            var title = world.Find<TextBlock>(text => text.Text == "Addons" && text.FontSize > 20);
            ClickAt(TopLevel.GetTopLevel(title)!, title.TranslatePoint(new Point(10, title.Bounds.Height / 2), TopLevel.GetTopLevel(title)!)!.Value);
            Assert.False(picker.Flyout.IsOpen);
        });

        world.Step(48, "Open AddOns folder opens the install's AddOns path", () =>
        {
            Click(picker);
            Click(MenuButton(world, "Open AddOns folder"));
            Assert.False(picker.Flyout!.IsOpen);
            Assert.Equal(world.BetaAddOns, Assert.Single(world.Opened));
        });

        world.Step(144, "Manage installs opens Settings with Installs expanded", () =>
        {
            Click(picker);
            Click(MenuButton(world, "Manage installs"));
            Assert.IsType<SettingsPage>(world.Window.CurrentPage);
            Assert.True(world.ByName<Control>("InstallsList").IsEffectivelyVisible);
        });
    });

    [Fact]
    public void P049_P050_copy_from_the_beta() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "copy-wizard");

        world.Step(49, "Copy from the beta wizard", () =>
        {
            Directory.CreateDirectory(Path.Combine(world.Live!, "WTF", "Account", "JIMMYBOBOZ"));
            Click(world.Find<Button>(button => button.Content as string == "Copy from beta"));
            var dialog = world.WaitForDialog();
            var text = Text(dialog);
            Assert.Contains("Copy from the beta", text, StringComparison.Ordinal);
            Assert.Contains("JIMMYBOBOZ copies from", text, StringComparison.Ordinal);
            Assert.Contains("1 file newer on the beta", text, StringComparison.Ordinal);
            Assert.Equal("Copy", dialog.PrimaryButtonText);
        });

        world.Step(50, "Escape closes the wizard, the banner stays, nothing recorded", () =>
        {
            world.Press(Key.Escape);
            world.WaitForNoDialog();
            Assert.NotNull(world.TryFind<Button>(button => button.Content as string == "Copy from beta"));
            Assert.Empty(world.Store.Load().ForeverCopyDone);
            Assert.False(Directory.Exists(Path.Combine(world.Live!, "Interface", "AddOns", "Details")));
        });
    });

    internal static Button MenuButton(UiPathWorld world, string text) =>
        world.Find<Button>(button => button.Classes.Contains("Menu") && Text(button).EndsWith(text, StringComparison.Ordinal));
}
