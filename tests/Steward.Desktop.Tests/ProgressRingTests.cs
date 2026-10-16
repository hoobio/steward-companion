using System.Runtime.InteropServices;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;

using Steward.Desktop.Views;

namespace Steward.Desktop.Tests;

public sealed class ProgressRingTests(AvaloniaSession session)
{
    [Theory]
    [InlineData(0.0, 0, 0)]
    [InlineData(0.25, 225, 90)]
    [InlineData(0.5, 450, 180)]
    [InlineData(0.75, 765, 90)]
    public void The_arc_grows_then_shrinks_while_it_turns_like_WinUI(double progress, double start, double sweep)
    {
        var frame = ProgressRing.Frame(progress);

        Assert.Equal(start, frame.Start, 9);
        Assert.Equal(sweep, frame.Sweep, 9);
    }

    [Fact]
    public void An_active_ring_draws_an_accent_arc_and_an_inactive_one_draws_nothing() => session.Run(() =>
    {
        var ring = new ProgressRing { Width = 64, Height = 64 };
        var window = new Window { Width = 100, Height = 100, Background = Brushes.Black, Content = ring };
        window.Show();

        Assert.True(AccentPixels(window) > 10);
        ring.IsActive = false;
        Assert.Equal(0, AccentPixels(window));
        window.Close();
    });

    private static int AccentPixels(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame()!;
        using var locked = frame.Lock();
        var pixels = new byte[locked.RowBytes * locked.Size.Height];
        Marshal.Copy(locked.Address, pixels, 0, pixels.Length);

        var (blue, red) = locked.Format == Avalonia.Platform.PixelFormat.Rgba8888 ? (2, 0) : (0, 2);
        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + blue] > 128 && pixels[i + blue] > pixels[i + red] + 64)
            {
                count++;
            }
        }

        return count;
    }
}
