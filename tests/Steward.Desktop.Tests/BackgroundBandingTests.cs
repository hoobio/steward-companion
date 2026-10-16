using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using Steward.Desktop.Views;

namespace Steward.Desktop.Tests;

public sealed class BackgroundBandingTests(AvaloniaSession session)
{
    [Fact]
    public void The_ground_glows_are_dithered_where_the_plain_brushes_band() => session.Run(() =>
    {
        var plain = Render(1280, 820, dithered: false);
        var dithered = Render(1280, 820, dithered: true);

        foreach (var (x0, y0, x1, y1) in Lines(1280, 820))
        {
            var before = Runs(plain, x0, y0, x1, y1);
            var after = Runs(dithered, x0, y0, x1, y1);
            Assert.True(before.MaxRun > 20);
            Assert.True(after.MaxRun <= 16);
            Assert.True(after.Changes >= 10 * before.Changes);
        }
    });

    [Fact]
    public void The_ground_glows_match_the_WinUI_baseline_colours() => session.Run(() =>
    {
        var dithered = Render(1264, 811, dithered: true);

        (int X, int Y, int Width, int Height, double R, double G, double B)[] regions =
        [
            (152, 7, 360, 32, 33.12, 43.65, 61.06),
            (752, 7, 360, 32, 38.48, 42.61, 56.87),
            (48, 169, 20, 310, 31.85, 39.05, 53.11),
            (1242, 209, 18, 210, 33.86, 38.41, 51.19),
            (752, 599, 480, 100, 31.00, 36.00, 48.00),
            (192, 49, 200, 38, 33.15, 43.93, 61.27),
        ];
        foreach (var region in regions)
        {
            var mean = Mean(dithered, region.X, region.Y, region.Width, region.Height);
            Assert.InRange(mean[0], region.R - 1, region.R + 1);
            Assert.InRange(mean[1], region.G - 1, region.G + 1);
            Assert.InRange(mean[2], region.B - 1, region.B + 1);
        }
    });

    [Theory]
    [InlineData(1264, 811)]
    [InlineData(1280, 820)]
    [InlineData(1920, 1230)]
    public void The_ground_renders_for_inspection(int width, int height) => session.Run(() =>
    {
        if (Environment.GetEnvironmentVariable("STEWARD_BANDING_PNG_DIR") is not { Length: > 0 } directory)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        foreach (var dithered in (bool[])[false, true])
        {
            using var frame = Capture(width, height, dithered);
            frame.Save(Path.Combine(directory, $"{(dithered ? "after" : "before")}-{width}x{height}.png"), PngBitmapEncoderOptions.Default);
        }
    });

    private static (int, int, int, int)[] Lines(int width, int height) =>
    [
        (0, 2, width * 6 / 10, 2),
        (0, 0, width * 45 / 100, height * 35 / 100),
        (width - 1, 2, width * 45 / 100, 2),
        (width - 1, 0, width * 65 / 100, height * 35 / 100),
    ];

    private static WriteableBitmap Capture(int width, int height, bool dithered)
    {
        var resources = Application.Current!;
        Control Glow(string key) => dithered
            ? new DitheredRadialGradient { Fill = (IRadialGradientBrush)resources.FindResource(key)! }
            : new Border { Background = (IBrush)resources.FindResource(key)! };
        var window = new Window
        {
            Width = width,
            Height = height,
            Background = (IBrush)resources.FindResource("WindowGroundBrush")!,
            Content = new Grid { Children = { Glow("GroundGlowTopLeft"), Glow("GroundGlowTopRight") } },
        };
        window.Show();
        var frame = window.CaptureRenderedFrame()!;
        window.Close();
        return frame;
    }

    private static (int Width, int[] Pixels) Render(int width, int height, bool dithered)
    {
        using var frame = Capture(width, height, dithered);
        using var locked = frame.Lock();
        var pixels = new int[locked.Size.Width * locked.Size.Height];
        for (var y = 0; y < locked.Size.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(locked.Address + (y * locked.RowBytes), pixels, y * locked.Size.Width, locked.Size.Width);
        }

        return (locked.Size.Width, pixels);
    }

    private static (int MaxRun, int Changes) Runs((int Width, int[] Pixels) image, int x0, int y0, int x1, int y1)
    {
        var steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        int run = 1, max = 1, changes = 0;
        var previous = image.Pixels[(y0 * image.Width) + x0];
        for (var i = 1; i <= steps; i++)
        {
            var pixel = image.Pixels[((y0 + ((y1 - y0) * i / steps)) * image.Width) + x0 + ((x1 - x0) * i / steps)];
            changes += pixel == previous ? 0 : 1;
            run = pixel == previous ? run + 1 : 1;
            max = Math.Max(max, run);
            previous = pixel;
        }

        return (max, changes);
    }

    private static double[] Mean((int Width, int[] Pixels) image, int x, int y, int width, int height)
    {
        var sums = new double[3];
        for (var j = y; j < y + height; j++)
        {
            for (var i = x; i < x + width; i++)
            {
                var pixel = image.Pixels[(j * image.Width) + i];
                for (var channel = 0; channel < 3; channel++)
                {
                    sums[channel] += (pixel >> (channel * 8)) & 0xFF;
                }
            }
        }

        return [.. sums.Select(sum => sum / (width * height))];
    }
}
