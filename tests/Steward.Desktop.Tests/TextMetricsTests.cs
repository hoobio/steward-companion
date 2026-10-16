using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Steward.Desktop.Styles;

namespace Steward.Desktop.Tests;

public sealed class TextMetricsTests(AvaloniaSession session)
{
    [Theory]
    [InlineData(12, 1, 15.961, 0.838)]
    [InlineData(13, 1, 17.291, 1.824)]
    [InlineData(14, 1, 18.621, 0.810)]
    [InlineData(20, 1, 26.602, 1.729)]
    [InlineData(13, 1.5, 17.291, 0.824)]
    public void Hanken_text_takes_the_Segoe_UI_line_box_and_WinUI_baseline(double size, double scale, double lineHeight, double shift)
    {
        Assert.Equal(lineHeight, SegoeMetrics.LineHeight("Hanken Grotesk", size), 3);
        Assert.Equal(shift, SegoeMetrics.BaselineShift("Hanken Grotesk", size, scale), 3);
    }

    [Theory]
    [InlineData(13, 1, 0.824)]
    [InlineData(14, 1, -0.190)]
    [InlineData(20, 1, 0.729)]
    [InlineData(14, 1.5, 0.477)]
    public void Boxed_Hanken_text_centres_its_capitals_at_most_a_pixel_above_the_WinUI_baseline(double size, double scale, double shift) =>
        Assert.Equal(shift, SegoeMetrics.BaselineShift("Hanken Grotesk", size, scale, free: false), 3);

    [Fact]
    public void Icons_take_the_Segoe_Fluent_Icons_line_box()
    {
        Assert.Equal(12, SegoeMetrics.LineHeight("FluentSystemIcons-Resizable", 12), 6);
        Assert.Equal(0, SegoeMetrics.BaselineShift("FluentSystemIcons-Resizable", 12, 1), 6);
    }

    [Fact]
    public void The_global_text_style_applies_the_calibration_to_bundled_fonts_only() => session.Run(() =>
    {
        var body = new TextBlock { Text = "Addons", FontSize = 14 };
        var glyph = new TextBlock { Text = "x", FontSize = 12, Classes = { "Glyph" } };
        var mono = new TextBlock { Text = "0.0.0", FontSize = 12, FontFamily = Resource<FontFamily>("MonoFontFamily") };
        var free = new TextBlock { Text = "Account", FontSize = 14 };
        var window = new Window { Content = new StackPanel { Children = { free, new StackPanel { Orientation = Orientation.Horizontal, Children = { body, glyph, mono } } } } };
        window.Show();
        try
        {
            Assert.Equal(18.621, body.LineHeight, 3);
            Assert.Equal(-0.190, Assert.IsType<TranslateTransform>(body.RenderTransform).Y, 3);
            Assert.Equal(0.810, Assert.IsType<TranslateTransform>(free.RenderTransform).Y, 3);
            Assert.Equal(19, body.Bounds.Height);
            Assert.Equal(12, glyph.LineHeight, 6);
            Assert.Equal(5d / 4, Assert.IsType<ScaleTransform>(glyph.RenderTransform).ScaleY, 6);
            Assert.True(double.IsNaN(mono.LineHeight));
            Assert.Null(mono.RenderTransform);
        }
        finally
        {
            window.Hide();
        }
    });

    private static T Resource<T>(string key) =>
        Application.Current!.TryGetResource(key, null, out var value) && value is T typed ? typed : throw new KeyNotFoundException(key);
}
