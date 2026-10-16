using Steward.Core;

namespace Steward.App.ViewModels;

public static class InitialsTile
{
    private static readonly uint[] Palette =
    [
        0xFFD4BFFF,
        0xFF409FFF,
        0xFF73D0FF,
        0xFFF29E74,
        0xFFE6B673,
        0xFFF28779,
        0xFFBAE67E,
        0xFFFFD580,
    ];

    public static string Text(string name) => string.Concat(name
        .Split([' ', '_', '-', '.', '!'], StringSplitOptions.RemoveEmptyEntries)
        .Take(2)
        .Select(word => char.ToUpperInvariant(word[0])));

    public static uint Color(string folderName) => Palette[InitialsColour.IndexFor(folderName, Palette.Length)];
}
