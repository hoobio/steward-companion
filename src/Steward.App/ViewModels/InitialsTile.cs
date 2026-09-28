using Steward.Core;

using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace Steward.App.ViewModels;

public static class InitialsTile
{
    private static readonly Color[] Palette =
    [
        Color.FromArgb(0xFF, 0xD4, 0xBF, 0xFF),
        Color.FromArgb(0xFF, 0x40, 0x9F, 0xFF),
        Color.FromArgb(0xFF, 0x73, 0xD0, 0xFF),
        Color.FromArgb(0xFF, 0xF2, 0x9E, 0x74),
        Color.FromArgb(0xFF, 0xE6, 0xB6, 0x73),
        Color.FromArgb(0xFF, 0xF2, 0x87, 0x79),
        Color.FromArgb(0xFF, 0xBA, 0xE6, 0x7E),
        Color.FromArgb(0xFF, 0xFF, 0xD5, 0x80),
    ];

    public static string Text(string name) => string.Concat(name
        .Split([' ', '_', '-', '.', '!'], StringSplitOptions.RemoveEmptyEntries)
        .Take(2)
        .Select(word => char.ToUpperInvariant(word[0])));

    public static Brush Brush(string folderName) => new SolidColorBrush(Palette[InitialsColour.IndexFor(folderName, Palette.Length)]);
}
