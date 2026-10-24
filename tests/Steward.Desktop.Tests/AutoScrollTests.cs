using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

using Steward.Desktop.Styles;

namespace Steward.Desktop.Tests;

public sealed class AutoScrollTests(AvaloniaSession session)
{
    [Fact]
    public void The_global_style_turns_it_on_for_every_scroll_viewer() => session.Run(() =>
    {
        var viewer = new ScrollViewer();
        var window = new Window { Content = viewer };
        window.Show();
        try
        {
            Assert.True(AutoScroll.GetIsEnabled(viewer));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void A_middle_press_enters_the_mode_on_a_scrollable_viewer_and_escape_exits() => session.Run(() =>
    {
        var (window, viewer) = Show(contentHeight: 2000);
        try
        {
            window.MouseDown(new Point(100, 100), MouseButton.Middle);
            Pump();
            Assert.Same(viewer, AutoScroll.Active);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Pump();
            Assert.Null(AutoScroll.Active);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void A_middle_press_on_a_viewer_that_cannot_scroll_does_nothing() => session.Run(() =>
    {
        var (window, _) = Show(contentHeight: 100);
        try
        {
            window.MouseDown(new Point(100, 100), MouseButton.Middle);
            Pump();
            Assert.Null(AutoScroll.Active);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void A_click_toggles_the_mode_on_and_a_second_press_exits() => session.Run(() =>
    {
        var (window, viewer) = Show(contentHeight: 2000);
        try
        {
            var point = new Point(100, 100);
            window.MouseDown(point, MouseButton.Middle);
            window.MouseUp(point, MouseButton.Middle);
            Pump();
            Assert.Same(viewer, AutoScroll.Active);

            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Pump();
            Assert.Null(AutoScroll.Active);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void A_hold_and_drag_exits_on_release() => session.Run(() =>
    {
        var (window, viewer) = Show(contentHeight: 2000);
        try
        {
            window.MouseDown(new Point(100, 100), MouseButton.Middle);
            window.MouseMove(new Point(100, 160), RawInputModifiers.MiddleMouseButton);
            Pump();
            Assert.Same(viewer, AutoScroll.Active);

            window.MouseUp(new Point(100, 160), MouseButton.Middle);
            Pump();
            Assert.Null(AutoScroll.Active);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void The_innermost_scrollable_viewer_takes_the_press() => session.Run(() =>
    {
        var inner = new ScrollViewer { Height = 200, Content = new Border { Height = 1000 } };
        var single = new ScrollViewer { Height = 100, Content = new Border { Height = 50 } };
        var outer = new ScrollViewer
        {
            Content = new StackPanel { Children = { inner, single, new Border { Height = 2000 } } },
        };
        var window = new Window { Width = 400, Height = 400, Content = outer };
        window.Show();
        Pump();
        try
        {
            window.MouseDown(new Point(100, 100), MouseButton.Middle);
            Pump();
            Assert.Same(inner, AutoScroll.Active);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.MouseUp(new Point(100, 100), MouseButton.Middle);

            window.MouseDown(new Point(100, 250), MouseButton.Middle);
            Pump();
            Assert.Same(outer, AutoScroll.Active);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Pump();
            Assert.Null(AutoScroll.Active);
        }
        finally
        {
            window.Close();
        }
    });

    [Theory]
    [InlineData(0, 0)]
    [InlineData(12, 0)]
    [InlineData(-12, 0)]
    [InlineData(24, 1.8284271247461903)]
    [InlineData(-36, -4.196152422706632)]
    [InlineData(48, 7)]
    public void The_speed_follows_firefoxs_curve(double distance, double pixelsPer20Ms) =>
        Assert.Equal(pixelsPer20Ms, AutoScroll.Speed(distance), 9);

    [Theory]
    [InlineData(5)]
    [InlineData(-11.9)]
    public void Nothing_scrolls_inside_the_dead_zone(double distance) =>
        Assert.Equal(300, AutoScroll.Step(300, distance, elapsedMilliseconds: 100, max: 1000));

    [Fact]
    public void A_step_scales_with_elapsed_time_caps_a_long_frame_and_stays_in_range()
    {
        Assert.Equal(300 + 7 * 2, AutoScroll.Step(300, 48, elapsedMilliseconds: 40, max: 1000), 9);
        Assert.Equal(300 + 7 * 5, AutoScroll.Step(300, 48, elapsedMilliseconds: 1000, max: 1000), 9);
        Assert.Equal(1000, AutoScroll.Step(999, 48, elapsedMilliseconds: 100, max: 1000));
        Assert.Equal(0, AutoScroll.Step(1, -48, elapsedMilliseconds: 100, max: 1000));
    }

    private static (Window Window, ScrollViewer Viewer) Show(double contentHeight)
    {
        var viewer = new ScrollViewer
        {
            Content = new Border { Height = contentHeight, VerticalAlignment = VerticalAlignment.Top },
        };
        var window = new Window { Width = 400, Height = 400, Content = viewer };
        window.Show();
        Pump();
        return (window, viewer);
    }

    private static void Pump()
    {
        for (var pass = 0; pass < 5; pass++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }
}
