using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using Steward.Desktop.Services;

namespace Steward.Desktop.Tests;

public sealed class TrayMenuFocusTests(AvaloniaSession session)
{
    [Fact]
    public void Escape_closes_the_tray_menu() => session.Run(() =>
    {
        var (window, _) = Open();
        var closed = false;
        window.Closed += (_, _) => closed = true;

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(closed);
    });

    [Fact]
    public void Down_selects_the_first_tray_menu_item() => session.Run(() =>
    {
        var (window, presenter) = Open();

        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, presenter.SelectedIndex);
        window.Close();
    });

    private static (Window Window, MenuFlyoutPresenter Presenter) Open()
    {
        TrayMenuFocus.Attach();
        var presenter = new HostClosingPresenter { ItemsSource = new[] { new MenuItem { Header = "Open Steward" }, new MenuItem { Header = "Quit" } } };
        var window = new Window { Name = "AvaloniaTrayPopupRoot_Steward", SizeToContent = SizeToContent.WidthAndHeight, Content = presenter };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, presenter);
    }

    private sealed class HostClosingPresenter : MenuFlyoutPresenter
    {
        protected override Type StyleKeyOverride => typeof(MenuFlyoutPresenter);

        public override void Close() => this.FindLogicalAncestorOfType<Window>()?.Close();
    }
}
