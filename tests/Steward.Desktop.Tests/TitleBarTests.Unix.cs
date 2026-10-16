using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;

using Steward.Desktop.Views;

namespace Steward.Desktop.Tests;

public sealed class TitleBarTests(AvaloniaSession session)
{
    [Fact]
    public void Double_click_on_the_title_row_maximises() => session.Run(() =>
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only: Avalonia.Native moves and zooms the window on macOS");
        using var host = session.NewHost();
        var window = host.Services.GetRequiredService<MainWindow>();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var titleRow = new Point(260, 24);

        Click(window, titleRow);
        Assert.Equal(WindowState.Normal, window.WindowState);

        Click(window, titleRow);
        Assert.Equal(WindowState.Maximized, window.WindowState);
    });

    private static void Click(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
