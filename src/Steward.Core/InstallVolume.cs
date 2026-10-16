using System.Text;

namespace Steward.Core;

public static class InstallVolume
{
    private static readonly string[] RemovableMediaRoots = ["/media", "/run/media", "/mnt"];

    public static bool IsPresent(string path) => OperatingSystem.IsLinux()
        ? IsPresent(path, MountPoints("/proc/self/mountinfo", 4), MountPoints("/etc/fstab", 1))
        : Directory.Exists(Path.GetPathRoot(path));

    // Linux has one root, so an unmounted disk leaves its path under "/"; a configured or removable-media mount that is not mounted counts as an absent drive.
    internal static bool IsPresent(string path, IReadOnlyCollection<string> mounted, IEnumerable<string> configured)
    {
        var holder = mounted.Where(mount => Contains(mount, path)).MaxBy(mount => mount.Length) ?? "/";
        if (configured.Any(mount => mount.Length > holder.Length && Contains(mount, path)))
        {
            return false;
        }

        return holder != "/" || !RemovableMediaRoots.Any(root => Contains(root, path));
    }

    internal static IEnumerable<string> ParseMountPoints(IEnumerable<string> lines, int field) =>
        lines.Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            .Where(fields => fields.Length > field && fields[field].StartsWith('/'))
            .Select(fields => Unescape(fields[field]).TrimEnd('/') is { Length: > 0 } mount ? mount : "/");

    private static string[] MountPoints(string file, int field)
    {
        try
        {
            return [.. ParseMountPoints(File.ReadLines(file), field)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool Contains(string mount, string path) =>
        mount == "/" || path == mount || path.StartsWith(mount + "/", StringComparison.Ordinal);

    // mountinfo and fstab write a space, tab, newline or backslash in a path as a three-digit octal escape.
    private static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && IsOctal(value, i + 1))
            {
                result.Append((char)Convert.ToInt32(value.Substring(i + 1, 3), 8));
                i += 3;
            }
            else
            {
                result.Append(value[i]);
            }
        }

        return result.ToString();
    }

    private static bool IsOctal(string value, int start) =>
        start + 3 <= value.Length && value.AsSpan(start, 3).IndexOfAnyExceptInRange('0', '7') < 0;
}
