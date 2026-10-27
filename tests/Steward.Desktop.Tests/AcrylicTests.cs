using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Steward.Desktop.Styles;

namespace Steward.Desktop.Tests;

public sealed class AcrylicTests(AvaloniaSession session)
{
    private static readonly Color Tint = Color.Parse("#FF0C0E13");
    private static readonly Color Fallback = Color.Parse("#FF111319");
    private static readonly Color Ground = Color.Parse("#FF181C26");

    [Fact]
    public void The_tint_is_the_dark_tint_at_57_percent() =>
        Assert.Equal(Color.Parse("#930C0E13"), Acrylic.Composite(Tint, null));

    [Fact]
    public void Saturation_follows_the_css_matrix() =>
        Assert.Equal(Color.Parse("#FF212B43"), Acrylic.Saturate(Color.Parse("#FF242B3C")));

    [Fact]
    public void A_flyout_draws_the_page_behind_it_blurred_under_the_tint() => session.Run(() =>
    {
        var block = new Border { Width = 120, Height = 60, Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(40, 60, 0, 0) };
        var content = new Border { Width = 240, Height = 320 };
        var button = new Button { Content = "Open", HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Flyout = new Flyout { FlyoutPresenterClasses = { "Tinted" }, Placement = PlacementMode.BottomEdgeAlignedLeft, Content = content } };
        var window = new Window { Width = 400, Height = 460, Background = new SolidColorBrush(Ground), Content = new Panel { Children = { block, button } } };
        window.Show();
        Pump();
        button.Flyout!.ShowAt(button);
        Pump();

        var presenter = Assert.IsType<FlyoutPresenter>(window.OpenedPopups.Single().Child);
        var root = Assert.IsType<Window>(TopLevel.GetTopLevel(presenter));
        Assert.Equal(AcrylicMode.Blur, Acrylic.GetMode(presenter));
        Assert.IsType<ImageBrush>(presenter.Background);

        using var frame = root.CaptureRenderedFrame()!;
        if (Environment.GetEnvironmentVariable("STEWARD_ACRYLIC_PNG") is { Length: > 0 } path)
        {
            frame.Save(path, PngBitmapEncoderOptions.Default);
        }

        var origin = content.TranslatePoint(default, root)!.Value;
        var blockOrigin = block.TranslatePoint(default, root)!.Value;
        var overBlock = Mean(frame, (int)blockOrigin.X + 30, (int)blockOrigin.Y + 20, 60, 20);
        var overGround = Mean(frame, (int)origin.X + 4, (int)origin.Y + 260, 220, 30);
        var expectedGround = Channels(Acrylic.Composite(Tint, Acrylic.Saturate(Ground)));
        var expectedWhite = Channels(Acrylic.Composite(Tint, Colors.White));
        for (var channel = 0; channel < 3; channel++)
        {
            Assert.InRange(overGround[channel], expectedGround[channel] - 2.0, expectedGround[channel] + 2.0);
            Assert.True(overGround[channel] <= Channels(Ground)[channel] - 5, "A flyout over plain page reads darker than the page.");
            Assert.InRange(overBlock[channel], expectedGround[channel] + 20, expectedWhite[channel] - 1);
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
        Assert.Equal(AcrylicMode.Blur, Acrylic.GetMode(presenter));

        window.Hide();
        Pump();
        Assert.Equal(AcrylicMode.Fallback, Acrylic.GetMode(presenter));
        Assert.Equal(Fallback, Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);

        window.Show();
        Pump();
        Assert.Equal(AcrylicMode.Blur, Acrylic.GetMode(presenter));
        window.Close();
    });

    [Fact]
    public void A_combo_box_list_draws_the_page_behind_it_blurred_under_the_tint() => session.Run(() =>
    {
        var block = new Border { Height = 200, Background = Brushes.White, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 260, 0, 0) };
        var combo = NewCombo();
        var window = new Window { Width = 400, Height = 460, Background = new SolidColorBrush(Ground), Content = new Panel { Children = { block, combo } } };
        window.Show();
        Pump();
        combo.IsDropDownOpen = true;
        Pump();

        var list = OpenList(combo);
        using var frame = window.CaptureRenderedFrame()!;
        if (Environment.GetEnvironmentVariable("STEWARD_ACRYLIC_PNG") is { Length: > 0 } path)
        {
            frame.Save(Path.Combine(Path.GetDirectoryName(path)!, "combo-" + Path.GetFileName(path)), PngBitmapEncoderOptions.Default);
        }

        var snapshot = Snapshot(list);
        var top = (int)list.TranslatePoint(default, window)!.Value.Y;
        var overGround = Mean(snapshot, 4, 150 - top, 190, 20);
        Assert.True(top + snapshot.PixelSize.Height >= 290, $"list from {top}, {snapshot.PixelSize.Height}px high");
        var overBlock = Mean(snapshot, 4, snapshot.PixelSize.Height - 24, 190, 20);
        var expectedGround = Channels(Acrylic.Composite(Tint, Acrylic.Saturate(Ground)));
        var expectedWhite = Channels(Acrylic.Composite(Tint, Colors.White));
        for (var channel = 0; channel < 3; channel++)
        {
            Assert.InRange(overGround[channel], expectedGround[channel] - 2.0, expectedGround[channel] + 2.0);
            Assert.InRange(overBlock[channel], expectedGround[channel] + 20, expectedWhite[channel] + 1);
        }

        combo.IsDropDownOpen = false;
        window.Close();
    });

    [Fact]
    public void A_combo_box_list_in_a_dialog_blurs_the_dialog_rather_than_the_page() => session.Run(() =>
    {
        var panel = Color.Parse("#FF2B3140");
        var combo = NewCombo();
        var window = new Window { Width = 400, Height = 460, Background = new SolidColorBrush(Ground) };
        window.Show();
        Pump();
        OverlayLayer.GetOverlayLayer(window)!.Children.Add(new Border { Width = 400, Height = 460, Background = new SolidColorBrush(panel), Child = combo });
        Pump();
        combo.IsDropDownOpen = true;
        Pump();

        var list = OpenList(combo);
        var top = (int)list.TranslatePoint(default, window)!.Value.Y;
        var overPanel = Mean(Snapshot(list), 4, 150 - top, 190, 20);
        var expected = Channels(Acrylic.Composite(Tint, Acrylic.Saturate(panel)));
        for (var channel = 0; channel < 3; channel++)
        {
            Assert.InRange(overPanel[channel], expected[channel] - 2.0, expected[channel] + 2.0);
        }

        combo.IsDropDownOpen = false;
        window.Close();
    });

    [Fact]
    public void A_border_falls_back_to_the_solid_colour_while_its_window_is_inactive() => session.Run(() =>
    {
        var surface = new Border { Width = 200, Height = 120 };
        Acrylic.SetIsEnabled(surface, true);
        var window = new Window { Width = 400, Height = 300, Content = surface };
        window.Show();
        Pump();
        Assert.Equal(AcrylicMode.Blur, Acrylic.GetMode(surface));
        Assert.IsType<ImageBrush>(surface.Background);

        window.Hide();
        Pump();
        Assert.Equal(AcrylicMode.Fallback, Acrylic.GetMode(surface));
        Assert.Equal(Fallback, Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color);
        window.Close();
    });

    private static ComboBox NewCombo() => new()
    {
        Width = 200,
        ItemsSource = Enumerable.Range(1, 8).Select(item => $"Item {item}").ToArray(),
        SelectedIndex = 0,
        Margin = new Thickness(0, 40, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Top,
    };

    private static Border OpenList(ComboBox combo)
    {
        var list = Assert.IsType<Border>(combo.GetVisualDescendants().OfType<Popup>().Single(popup => popup.Name == "PART_Popup").Child);
        Assert.Equal(AcrylicMode.Blur, Acrylic.GetMode(list));
        Assert.Equal(new CornerRadius(8), list.CornerRadius);
        Assert.Equal(default, list.BorderThickness);
        return list;
    }

    private static WriteableBitmap Snapshot(Border list) =>
        Assert.IsType<WriteableBitmap>(Assert.IsType<ImageBrush>(list.Background).Source);

    private static double[] Channels(Color color) => [color.R, color.G, color.B];

    private static double[] Mean(WriteableBitmap frame, int x, int y, int width, int height)
    {
        using var locked = frame.Lock();
        var rgba = locked.Format == Avalonia.Platform.PixelFormat.Rgba8888;
        var sums = new double[3];
        var row = new int[width];
        for (var j = y; j < y + height; j++)
        {
            System.Runtime.InteropServices.Marshal.Copy(locked.Address + (j * locked.RowBytes) + (x * 4), row, 0, width);
            foreach (var pixel in row)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    sums[channel] += (pixel >> ((rgba ? channel : 2 - channel) * 8)) & 0xFF;
                }
            }
        }

        return sums.Select(sum => sum / ((double)width * height)).ToArray();
    }

    private static void Pump()
    {
        for (var pass = 0; pass < 5; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
}
