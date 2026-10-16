using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Steward.App.Views;

public static class BindConvert
{
    private static readonly SolidColorBrush Transparent = new(Colors.Transparent);

    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Brush Brush(string? resourceKey) => resourceKey is null ? Transparent : (Brush)Application.Current.Resources[resourceKey];

    public static Style Style(string resourceKey) => (Style)Application.Current.Resources[resourceKey];

    public static Brush ColorBrush(uint argb) => new SolidColorBrush(Windows.UI.Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));

    public static ImageSource? Image(object? image) => image as ImageSource;

    public static Thickness TopHairline(bool shown) => shown ? new Thickness(0, 1, 0, 0) : default;

    public static string Glyph(string? name) => name switch
    {
        ViewModels.Glyphs.Info => "",
        ViewModels.Glyphs.CheckMark => "",
        ViewModels.Glyphs.Warning => "",
        ViewModels.Glyphs.Error => "",
        ViewModels.Glyphs.Lock => "",
        ViewModels.Glyphs.ChevronUp => "",
        ViewModels.Glyphs.ChevronDown => "",
        _ => "",
    };
}
