using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Steward.Desktop.Views;

public static class BindConvert
{
    public static readonly IValueConverter Brush = new FuncValueConverter<string?, IBrush?>(key =>
        key is null ? Brushes.Transparent : Resource<IBrush>(key));

    public static readonly IValueConverter Theme = new FuncValueConverter<string?, ControlTheme?>(key =>
        key is null ? null : Resource<ControlTheme>(key));

    public static readonly IValueConverter ColorBrush = new FuncValueConverter<uint, IBrush>(argb =>
        new SolidColorBrush(Color.FromUInt32(argb)));

    public static readonly IValueConverter TopHairline = new FuncValueConverter<bool, Thickness>(shown =>
        shown ? new Thickness(0, 1, 0, 0) : default);

    public static readonly IValueConverter Image = new FuncValueConverter<object?, IImage?>(image => image as IImage);

    public static readonly IValueConverter Glyph = new FuncValueConverter<string?, string>(name =>
        string.IsNullOrEmpty(name) ? "" : Resource<string>($"Glyph.{name}") ?? "");

    public static readonly IValueConverter Initials = new FuncValueConverter<string?, string>(name =>
    {
        var words = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length switch
        {
            0 => "",
            1 => words[0][..1].ToUpperInvariant(),
            _ => $"{words[0][..1]}{words[^1][..1]}".ToUpperInvariant(),
        };
    });

    private static T? Resource<T>(string key) where T : class =>
        Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) == true ? value as T : null;
}
