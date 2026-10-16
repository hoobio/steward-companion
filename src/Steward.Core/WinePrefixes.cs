using System.Text.RegularExpressions;

namespace Steward.Core;

public static partial class WinePrefixes
{
    private static readonly string[] ProgramFolders = ["Program Files (x86)", "Program Files"];

    public static IEnumerable<string> WowRoots(string home, Func<string, string?> environment) =>
        Prefixes(home, environment)
            .SelectMany(prefix => ProgramFolders.Select(folder => FindIgnoringCase(prefix, "drive_c", folder, "World of Warcraft")))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal);

    internal static IEnumerable<string> Prefixes(string home, Func<string, string?> environment)
    {
        if (environment("WINEPREFIX") is { Length: > 0 } winePrefix)
        {
            yield return winePrefix;
        }

        yield return Path.Combine(home, ".wine");

        foreach (var config in LutrisConfigs(home, environment))
        {
            if (LutrisPrefix(ReadOrEmpty(config), home) is { } prefix)
            {
                yield return prefix;
            }
        }

        var data = DataHome(home, environment);
        foreach (var parent in new[]
        {
            Path.Combine(home, "Games"),
            Path.Combine(home, "Games", "Heroic", "Prefixes", "default"),
            Path.Combine(home, "Faugus"),
            Path.Combine(data, "bottles", "bottles"),
            Path.Combine(home, ".var", "app", "com.usebottles.bottles", "data", "bottles", "bottles"),
        })
        {
            foreach (var child in Children(parent))
            {
                yield return child;
            }
        }

        foreach (var steam in new[]
        {
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(data, "Steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
        })
        {
            foreach (var game in Children(Path.Combine(steam, "steamapps", "compatdata")))
            {
                yield return Path.Combine(game, "pfx");
            }
        }
    }

    public static IEnumerable<string> LutrisConfigs(string home, Func<string, string?> environment)
    {
        var config = environment("XDG_CONFIG_HOME") is { Length: > 0 } configHome ? configHome : Path.Combine(home, ".config");
        var flatpak = Path.Combine(home, ".var", "app", "net.lutris.Lutris");
        return new[]
        {
            Path.Combine(config, "lutris", "games"),
            Path.Combine(DataHome(home, environment), "lutris", "games"),
            Path.Combine(flatpak, "config", "lutris", "games"),
            Path.Combine(flatpak, "data", "lutris", "games"),
        }.SelectMany(folder => Files(folder, "*.yml"));
    }

    internal static string? LutrisPrefix(string yaml, string home)
    {
        var value = PrefixLine().Match(yaml) is { Success: true } match ? match.Groups[1].Value.Trim().Trim('"', '\'') : null;
        if (value?.StartsWith('~') == true)
        {
            value = home + value[1..];
        }

        return value is { Length: > 0 } && Path.IsPathRooted(value) ? value : null;
    }

    public static string? ToUnixPath(string windowsPath, string prefix)
    {
        if (windowsPath.Length < 3 || !char.IsAsciiLetter(windowsPath[0]) || windowsPath[1] != ':' || windowsPath[2] is not ('\\' or '/'))
        {
            return null;
        }

        var drive = char.ToLowerInvariant(windowsPath[0]);
        var root = drive switch
        {
            'c' => Path.Combine(prefix, "drive_c"),
            'z' => "/",
            _ => DosDevice(prefix, drive),
        };
        return root is null ? null : Path.Combine([root, .. windowsPath[3..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)]);
    }

    internal static string? FindIgnoringCase(string root, params string[] segments)
    {
        var current = root;
        foreach (var segment in segments)
        {
            var exact = Path.Combine(current, segment);
            current = Directory.Exists(exact)
                ? exact
                : Children(current).FirstOrDefault(child => string.Equals(Path.GetFileName(child), segment, StringComparison.OrdinalIgnoreCase));
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    private static string DataHome(string home, Func<string, string?> environment) =>
        environment("XDG_DATA_HOME") is { Length: > 0 } dataHome ? dataHome : Path.Combine(home, ".local", "share");

    private static string? DosDevice(string prefix, char drive)
    {
        try
        {
            return new DirectoryInfo(Path.Combine(prefix, "dosdevices", $"{drive}:")).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> Children(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? [.. Directory.EnumerateDirectories(folder)] : [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> Files(string folder, string pattern)
    {
        try
        {
            return Directory.Exists(folder) ? [.. Directory.EnumerateFiles(folder, pattern)] : [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string ReadOrEmpty(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    [GeneratedRegex(@"^\s+prefix:\s*(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex PrefixLine();
}
