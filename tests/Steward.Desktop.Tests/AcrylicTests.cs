using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using Steward.Desktop.Styles;

namespace Steward.Desktop.Tests;

public sealed class AcrylicTests(AvaloniaSession session)
{
    private static readonly Color Tint = Color.Parse("#FF232834");
    private static readonly Color Fallback = Color.Parse("#FF2B3140");

    [Fact]
    public void The_blur_overlay_is_the_tint_at_the_combined_luminosity_and_tint_opacity() =>
        Assert.Equal(Color.Parse("#F6232834"), Acrylic.Composite(Tint, null));

    [Fact]
    public void The_noise_has_the_six_binomial_levels_of_the_WinUI_texture()
    {
        var levels = Acrylic.NoiseLevels();
        var counts = levels.GroupBy(level => level).OrderBy(group => group.Key).ToDictionary(group => group.Key, group => group.Count() / (double)levels.Length);
        Assert.Equal([0, 51, 102, 153, 204, 255], counts.Keys.Select(key => (int)key));
        double[] binomial = [1 / 32d, 5 / 32d, 10 / 32d, 10 / 32d, 5 / 32d, 1 / 32d];
        Assert.All(counts.Values.Zip(binomial), pair => Assert.InRange(pair.First, pair.Second * 0.9, pair.Second * 1.1));
    }

    [Fact]
    public void An_overlay_flyout_draws_mock_acrylic_within_a_level_of_WinUI_acrylic() => session.Run(() =>
    {
        var content = new Border { Width = 200, Height = 120 };
        var button = new Button { Content = "Open", Flyout = new Flyout { FlyoutPresenterClasses = { "Tinted" }, Placement = PlacementMode.Bottom, Content = content } };
        var window = new Window { Width = 400, Height = 300, Content = button };
        window.Show();
        Pump();
        button.Flyout!.ShowAt(button);
        Pump();

        var presenter = Assert.IsType<FlyoutPresenter>(window.OpenedPopups.Single().Child);
        var root = Assert.IsType<Window>(TopLevel.GetTopLevel(presenter));
        Assert.Equal(AcrylicMode.Mock, Acrylic.GetMode(presenter));

        using var frame = root.CaptureRenderedFrame()!;
        if (Environment.GetEnvironmentVariable("STEWARD_ACRYLIC_PNG") is { Length: > 0 } path)
        {
            frame.Save(path, PngBitmapEncoderOptions.Default);
        }

        var origin = content.TranslatePoint(default, root)!.Value;
        var (mean, deviation) = Stats(frame, (int)origin.X + 4, (int)origin.Y + 4, 192, 112);
        var expected = new[] { Tint.R, Tint.G, Tint.B };
        for (var channel = 0; channel < 3; channel++)
        {
            Assert.InRange(mean[channel], expected[channel] - 1.0, expected[channel] + 1.0);
            Assert.InRange(deviation[channel], 0.5, 2.0);
        }

        button.Flyout.Hide();
        window.Close();
    });

    [Fact]
    public void An_overlay_presenter_falls_back_to_the_solid_colour_while_its_window_is_inactive() => session.Run(() =>
    {
        var presenter = new FlyoutPresenter { Classes = { "Tinted" }, Content = new Border { Width = 200, Height = 120 } };
        var window = new Window { Width = 400, Height = 300, Content = presenter };
        window.Show();
        Pump();
        Assert.True(window.IsActive);
        Assert.Equal(AcrylicMode.Mock, Acrylic.GetMode(presenter));
        Assert.IsType<ImageBrush>(presenter.Background);

        window.Hide();
        Pump();
        Assert.Equal(AcrylicMode.Fallback, Acrylic.GetMode(presenter));
        Assert.Equal(Fallback, Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);

        window.Show();
        Pump();
        Assert.Equal(AcrylicMode.Mock, Acrylic.GetMode(presenter));
        window.Close();
    });

    private static (double[] Mean, double[] Deviation) Stats(WriteableBitmap frame, int x, int y, int width, int height)
    {
        using var locked = frame.Lock();
        var rgba = locked.Format == Avalonia.Platform.PixelFormat.Rgba8888;
        var sums = new double[3];
        var squares = new double[3];
        var row = new int[width];
        for (var j = y; j < y + height; j++)
        {
            System.Runtime.InteropServices.Marshal.Copy(locked.Address + (j * locked.RowBytes) + (x * 4), row, 0, width);
            foreach (var pixel in row)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    double value = (pixel >> ((rgba ? channel : 2 - channel) * 8)) & 0xFF;
                    sums[channel] += value;
                    squares[channel] += value * value;
                }
            }
        }

        var count = (double)width * height;
        var mean = sums.Select(sum => sum / count).ToArray();
        return (mean, squares.Select((square, channel) => Math.Sqrt(Math.Max(0, square / count - mean[channel] * mean[channel]))).ToArray());
    }

    private static void Pump()
    {
        for (var pass = 0; pass < 5; pass++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }
}
