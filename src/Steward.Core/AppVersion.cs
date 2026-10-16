using System.Globalization;

namespace Steward.Core;

public static class AppVersion
{
    public static int? Compare(string left, string right) =>
        Parse(left) is { } a && Parse(right) is { } b ? CompareParsed(a, b) : null;

    public static bool IsNewer(string available, string installed) => Compare(available, installed) > 0;

    public static bool IsPreRelease(string version) => Parse(version) is { PreRelease.Length: > 0 };

    // dpkg and rpm spell a semver pre-release with ~ (0.23.2~preview.31), which sorts below 0.23.2 the same way.
    public static string FromPackageVersion(string packageVersion) => packageVersion.Trim().Replace('~', '-');

    private sealed record Parsed(int[] Numbers, string[] PreRelease);

    private static Parsed? Parse(string value)
    {
        var text = value.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        text = text.Split('+', 2)[0];
        var dash = text.IndexOf('-', StringComparison.Ordinal);
        var core = dash < 0 ? text : text[..dash];
        var preRelease = dash < 0 ? [] : text[(dash + 1)..].Split('.');
        if (preRelease.Any(part => part.Length == 0))
        {
            return null;
        }

        var parts = core.Split('.');
        if (parts.Length is < 3 or > 4)
        {
            return null;
        }

        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit)
                || !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return null;
            }
        }

        return new Parsed(numbers, preRelease);
    }

    private static int CompareParsed(Parsed a, Parsed b)
    {
        for (var i = 0; i < 4; i++)
        {
            if (a.Numbers[i] != b.Numbers[i])
            {
                return a.Numbers[i].CompareTo(b.Numbers[i]);
            }
        }

        if (a.PreRelease.Length == 0 || b.PreRelease.Length == 0)
        {
            return a.PreRelease.Length == b.PreRelease.Length ? 0 : a.PreRelease.Length == 0 ? 1 : -1;
        }

        for (var i = 0; i < Math.Min(a.PreRelease.Length, b.PreRelease.Length); i++)
        {
            var order = CompareIdentifier(a.PreRelease[i], b.PreRelease[i]);
            if (order != 0)
            {
                return order;
            }
        }

        return a.PreRelease.Length.CompareTo(b.PreRelease.Length);
    }

    private static int CompareIdentifier(string a, string b)
    {
        var aNumeric = a.All(char.IsAsciiDigit);
        var bNumeric = b.All(char.IsAsciiDigit);
        return (aNumeric, bNumeric) switch
        {
            (true, true) => a.TrimStart('0').Length != b.TrimStart('0').Length
                ? a.TrimStart('0').Length.CompareTo(b.TrimStart('0').Length)
                : string.CompareOrdinal(a.TrimStart('0'), b.TrimStart('0')),
            (true, false) => -1,
            (false, true) => 1,
            _ => string.CompareOrdinal(a, b),
        };
    }
}
