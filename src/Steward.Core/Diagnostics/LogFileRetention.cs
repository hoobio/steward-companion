using System.Globalization;

namespace Steward.Core.Diagnostics;

public static class LogFileRetention
{
    public const string Prefix = "steward-";
    public const string Extension = ".log";
    private const string DateFormat = "yyyyMMdd";

    public static void DeleteOlderThan(string directory, int retentionDays, DateTimeOffset now)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        var cutoff = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-retentionDays);
        foreach (var file in Directory.EnumerateFiles(directory, $"{Prefix}*{Extension}"))
        {
            if (TryParseDate(Path.GetFileName(file), out var date) && date < cutoff)
            {
                File.Delete(file);
            }
        }
    }

    public static string FileNameFor(DateOnly date) => $"{Prefix}{date.ToString(DateFormat, CultureInfo.InvariantCulture)}{Extension}";

    public static bool TryParseDate(string fileName, out DateOnly date)
    {
        date = default;
        if (!fileName.StartsWith(Prefix, StringComparison.Ordinal) || !fileName.EndsWith(Extension, StringComparison.Ordinal))
        {
            return false;
        }

        var stem = fileName[Prefix.Length..^Extension.Length];
        return DateOnly.TryParseExact(stem, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
