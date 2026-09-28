namespace Steward.Core;

public static class TocFile
{
    public static string? ReadVersion(string tocPath) => ReadDirective(tocPath, "Version");

    public static string? ReadDirective(string tocPath, string directive)
    {
        if (!File.Exists(tocPath))
        {
            return null;
        }

        var prefix = $"## {directive}:";
        foreach (var line in File.ReadLines(tocPath))
        {
            var trimmed = line.TrimEnd('\r');
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

    public static int? InterfaceNumber(string? clientVersion)
    {
        if (clientVersion is null)
        {
            return null;
        }

        var parts = clientVersion.Split('.');
        if (parts.Length < 3
            || !int.TryParse(parts[0], out var major)
            || !int.TryParse(parts[1], out var minor)
            || !int.TryParse(parts[2], out var patch))
        {
            return null;
        }

        return (major * 10000) + (minor * 100) + patch;
    }

    public static bool MatchesInterface(string? interfaceDirective, int clientInterface)
    {
        if (string.IsNullOrEmpty(interfaceDirective))
        {
            return false;
        }

        return interfaceDirective
            .Split(',')
            .Any(value => int.TryParse(value.Trim(), out var parsed) && parsed == clientInterface);
    }

    public static string FormatInterface(int value) =>
        $"{value / 10000}.{value / 100 % 100}.{value % 100}";
}
