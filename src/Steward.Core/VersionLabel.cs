namespace Steward.Core;

public static class VersionLabel
{
    public const int MaxLength = 16;

    public static string? For(string? version, string folderName) => Shorten(Strip(version, folderName));

    private static string? Shorten(string? label) =>
        label is { Length: > MaxLength } ? $"{label[..9]}…{label[^6..]}" : label;

    private static string? Strip(string? version, string folderName)
    {
        if (version is null)
        {
            return null;
        }

        var label = version.Replace("-pre-release.", "-", StringComparison.Ordinal);
        if (label.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            label = label[..^4];
        }

        var firstDigit = label.AsSpan().IndexOfAnyInRange('0', '9');
        return label.StartsWith(folderName, StringComparison.OrdinalIgnoreCase) && firstDigit > 0 ? label[firstDigit..] : label;
    }
}
