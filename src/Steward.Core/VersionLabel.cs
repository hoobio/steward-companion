namespace Steward.Core;

public static class VersionLabel
{
    public static string? For(string? version, string folderName)
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
