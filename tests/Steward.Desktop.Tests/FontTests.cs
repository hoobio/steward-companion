using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Steward.Desktop.Tests;

public sealed class FontTests(AvaloniaSession session)
{
    [Theory]
    [InlineData("ContentControlThemeFontFamily", "Hanken Grotesk")]
    [InlineData("DisplayFontFamily", "Hanken Grotesk")]
    [InlineData("MonoFontFamily", "Cascadia Mono")]
    [InlineData("IconFontFamily", "FluentSystemIcons-Resizable")]
    public void Font_resources_load_the_bundled_font(string key, string familyName) => session.Run(() =>
    {
        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(Resource<FontFamily>(key)), out var typeface));
        Assert.Equal(familyName, typeface.FamilyName);
    });

    [Fact]
    public void Every_glyph_is_in_the_icon_font() => session.Run(() =>
    {
        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(Resource<FontFamily>("IconFontFamily")), out var typeface));
        var glyphs = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://Steward.Desktop/Styles/Glyphs.axaml"));
        var keys = glyphs.Keys.OfType<string>().Where(key => key.StartsWith("Glyph.", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.NotEqual(0, typeface.CharacterToGlyphMap.GetGlyph(char.ConvertToUtf32(Resource<string>(key), 0))));
    });

    [Fact]
    public void Text_weights_render_at_different_widths() => session.Run(() =>
    {
        var family = Resource<FontFamily>("ContentControlThemeFontFamily");
        Assert.All([FontWeight.Normal, FontWeight.SemiBold, FontWeight.Bold], weight =>
        {
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(family, FontStyle.Normal, weight), out var typeface));
            Assert.Equal((weight, FontSimulations.None), (typeface.Weight, typeface.FontSimulations));
        });
        double Width(FontWeight weight) => new FormattedText(
            "Steward Addons Settings", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyle.Normal, weight), 14, null).Width;

        Assert.True(Width(FontWeight.SemiBold) > Width(FontWeight.Normal));
        Assert.True(Width(FontWeight.Bold) > Width(FontWeight.SemiBold));
    });

    private static T Resource<T>(string key) =>
        Application.Current!.TryGetResource(key, null, out var value) && value is T typed ? typed : throw new KeyNotFoundException(key);
}
