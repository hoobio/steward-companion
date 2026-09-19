using Microsoft.Win32;

namespace Steward.Core;

public static class WowInstalls
{
    private static readonly Dictionary<string, string> FlavourDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["_classic_beta_"] = "World of Warcraft: Forever - Beta",
        ["_retail_"] = "World of Warcraft",
    };

    public static string DisplayName(string flavour) =>
        FlavourDisplayNames.GetValueOrDefault(flavour, flavour);

    public static IReadOnlyList<WowInstall> Discover()
    {
        var installs = new List<WowInstall>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in CandidateRoots())
        {
            foreach (var install in DiscoverAt(root))
            {
                if (seen.Add(install.FlavourPath))
                {
                    installs.Add(install);
                }
            }
        }

        return installs;
    }

    internal static IEnumerable<WowInstall> DiscoverAt(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var flavourPath in ValidFlavourDirectories(root))
        {
            yield return BuildInstall(root, flavourPath);
        }
    }

    public static WowInstall? FromFlavourPath(string flavourPath)
    {
        if (!IsValidFlavourDirectory(flavourPath))
        {
            return null;
        }

        var root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(flavourPath)) ?? flavourPath;
        return BuildInstall(root, flavourPath);
    }

    private static IEnumerable<string> CandidateRoots()
    {
        // Registry InstallPath is not trustworthy on its own: on the dev machine that key
        // is hijacked by an unrelated Ascension install pointing at C:\Program Files (x86)\ascension-live\.
        foreach (var registryRoot in RegistryRoots())
        {
            yield return registryRoot;
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
            {
                continue;
            }

            var root = drive.RootDirectory.FullName;
            yield return Path.Combine(root, "World of Warcraft");
            yield return Path.Combine(root, "Program Files", "World of Warcraft");
            yield return Path.Combine(root, "Program Files (x86)", "World of Warcraft");
        }
    }

    private static IEnumerable<string> RegistryRoots()
    {
        foreach (var keyPath in new[]
        {
            @"SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft",
            @"SOFTWARE\Blizzard Entertainment\World of Warcraft",
        })
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key?.GetValue("InstallPath") is string installPath && installPath.Length > 0)
            {
                yield return installPath;
            }
        }
    }

    private static IEnumerable<string> ValidFlavourDirectories(string root)
    {
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            if (IsValidFlavourDirectory(dir))
            {
                yield return dir;
            }
        }
    }

    private static bool IsValidFlavourDirectory(string flavourPath)
    {
        if (!Directory.Exists(flavourPath))
        {
            return false;
        }

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(flavourPath));
        if (name.Length < 3 || !name.StartsWith('_') || !name.EndsWith('_'))
        {
            return false;
        }

        return Directory.Exists(Path.Combine(flavourPath, "Interface", "AddOns"));
    }

    private static WowInstall BuildInstall(string root, string flavourPath)
    {
        var flavour = Path.GetFileName(Path.TrimEndingDirectorySeparator(flavourPath));
        var addOnsPath = Path.Combine(flavourPath, "Interface", "AddOns");
        var productCode = ReadProductCode(flavourPath);
        var clientVersion = ReadClientVersion(root, productCode);

        return new WowInstall(root, flavour, flavourPath, addOnsPath, productCode, clientVersion);
    }

    private static string? ReadProductCode(string flavourPath)
    {
        var flavorInfoPath = Path.Combine(flavourPath, ".flavor.info");
        if (!File.Exists(flavorInfoPath))
        {
            return null;
        }

        var lines = File.ReadAllLines(flavorInfoPath);
        return lines.Length >= 2 ? lines[1].Trim() : null;
    }

    private static string? ReadClientVersion(string root, string? productCode)
    {
        if (productCode is null)
        {
            return null;
        }

        var buildInfoPath = Path.Combine(root, ".build.info");
        if (!File.Exists(buildInfoPath))
        {
            return null;
        }

        var lines = File.ReadAllLines(buildInfoPath);
        if (lines.Length < 2)
        {
            return null;
        }

        var columns = lines[0].Split('|');
        var versionIndex = -1;
        var productIndex = -1;
        for (var i = 0; i < columns.Length; i++)
        {
            var columnName = columns[i].Split('!')[0];
            if (string.Equals(columnName, "Version", StringComparison.Ordinal))
            {
                versionIndex = i;
            }
            else if (string.Equals(columnName, "Product", StringComparison.Ordinal))
            {
                productIndex = i;
            }
        }

        if (versionIndex < 0 || productIndex < 0)
        {
            return null;
        }

        for (var row = 1; row < lines.Length; row++)
        {
            var fields = lines[row].Split('|');
            if (fields.Length <= Math.Max(versionIndex, productIndex))
            {
                continue;
            }

            if (string.Equals(fields[productIndex], productCode, StringComparison.Ordinal))
            {
                return fields[versionIndex];
            }
        }

        return null;
    }
}
