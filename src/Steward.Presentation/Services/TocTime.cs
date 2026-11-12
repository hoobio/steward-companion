using System.Globalization;

using Steward.Core;

namespace Steward.App.Services;

public static class TocTime
{
    public static DateTimeOffset? LastWrite(string addOnsPath, string folderName)
    {
        var folder = Path.Combine(addOnsPath, folderName);
        var exact = Path.Combine(folder, folderName + ".toc");
        try
        {
            var toc = File.Exists(exact)
                ? exact
                : Directory.Exists(folder) ? LocalAddons.FlavourTocs(folder, folderName).FirstOrDefault() : null;
            return toc is null ? null : new DateTimeOffset(File.GetLastWriteTimeUtc(toc), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string? Describe(string verb, DateTimeOffset? at) => at is { } time
        ? $"{verb} {RelativeTime.Describe(time, DateTimeOffset.Now)} ({time.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture)})"
        : null;
}
