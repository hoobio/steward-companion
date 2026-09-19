namespace Steward.Core;

public static class TocFile
{
    public static string? ReadVersion(string tocPath)
    {
        if (!File.Exists(tocPath))
        {
            return null;
        }

        foreach (var line in File.ReadLines(tocPath))
        {
            var trimmed = line.TrimEnd('\r');
            const string prefix = "## Version:";
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var value = trimmed[prefix.Length..].Trim();
            return value.Length == 0 ? null : value;
        }

        return null;
    }

    public static bool HasUpdate(string available, string? installed)
    {
        if (installed is null)
        {
            return true;
        }

        return !string.Equals(available, installed, StringComparison.OrdinalIgnoreCase);
    }
}
