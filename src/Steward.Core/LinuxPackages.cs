using System.Runtime.InteropServices;

namespace Steward.Core;

public enum LinuxPackageKind
{
    None,
    Deb,
    Rpm,
}

public sealed record LinuxInstall(LinuxPackageKind Kind, string? FeedSource)
{
    public bool FromFeed => FeedSource is not null;
}

public sealed record CommandResult(int ExitCode, string Output);

public enum PackageInstallOutcome
{
    Installed,
    Dismissed,
    NoAuthenticationAgent,
    NotAuthorized,
    Failed,
}

public static class LinuxPackages
{
    public const string PackageName = "steward";

    public const string FeedHost = "hoobio.github.io/steward-companion";

    public const string Pkexec = "pkexec";

    // One pkexec, so one password prompt covers refreshing the Steward source and upgrading from it; the values arrive as $1, never in the script.
    internal const string AptFeedScript =
        "export DEBIAN_FRONTEND=noninteractive; "
        + "apt-get update -o Dir::Etc::sourcelist=\"$1\" -o Dir::Etc::sourceparts=- -o APT::Get::List-Cleanup=0 "
        + "&& apt-get install -y --only-upgrade " + PackageName;

    // A same-train launch hands over to the running instance and exits, so the new binary starts once this process has gone or is a zombie nobody reaps.
    internal const string RelaunchScript =
        "while [ -r \"/proc/$1/stat\" ] && [ \"$(sed 's/.*) //;s/ .*//' \"/proc/$1/stat\" 2>/dev/null)\" != Z ]; do sleep 0.2; done; exec \"$2\"";

    public static LinuxInstall Detect(string exePath, Func<string, IReadOnlyList<string>, CommandResult?> run, string etc = "/etc")
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run("dpkg-query", ["-S", exePath]) is { ExitCode: 0 } deb
            && deb.Output.StartsWith($"{PackageName}:", StringComparison.Ordinal))
        {
            return new LinuxInstall(LinuxPackageKind.Deb, FeedFile(Path.Combine(etc, "apt", "sources.list.d"), ".list", ".sources"));
        }

        if (run("rpm", ["-qf", "--queryformat", "%{NAME}", exePath]) is { ExitCode: 0 } rpm
            && rpm.Output.Trim() == PackageName)
        {
            return new LinuxInstall(LinuxPackageKind.Rpm, FeedFile(Path.Combine(etc, "yum.repos.d"), ".repo"));
        }

        return new LinuxInstall(LinuxPackageKind.None, null);
    }

    private static string? FeedFile(string directory, params string[] extensions)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        return Directory.EnumerateFiles(directory)
            .Where(file => extensions.Contains(Path.GetExtension(file), StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault(file => File.ReadLines(file)
                .Any(line => !line.TrimStart().StartsWith('#') && line.Contains(FeedHost, StringComparison.Ordinal)));
    }

    public static string? AssetSuffix(LinuxPackageKind kind, Architecture architecture) => architecture switch
    {
        Architecture.X64 => $"-linux-x64.{(kind == LinuxPackageKind.Rpm ? "rpm" : "deb")}",
        Architecture.Arm64 => $"-linux-arm64.{(kind == LinuxPackageKind.Rpm ? "rpm" : "deb")}",
        _ => null,
    };

    public static string PackageExtension(LinuxPackageKind kind) => kind == LinuxPackageKind.Rpm ? ".rpm" : ".deb";

    public static IReadOnlyList<string> InstallArguments(LinuxInstall install, string? packagePath, bool elevated)
    {
        ArgumentNullException.ThrowIfNull(install);
        IReadOnlyList<string> command = (install.Kind, install.FeedSource, packagePath) switch
        {
            (LinuxPackageKind.Deb, { } source, _) => ["/bin/sh", "-c", AptFeedScript, "steward-update", source],
            (LinuxPackageKind.Rpm, not null, _) => ["/usr/bin/dnf", "upgrade", "-y", "--refresh", PackageName],
            (LinuxPackageKind.Deb, null, { } path) when Path.IsPathRooted(path) => ["/usr/bin/apt-get", "install", "-y", path],
            (LinuxPackageKind.Rpm, null, { } path) when Path.IsPathRooted(path) => ["/usr/bin/dnf", "install", "-y", path],
            _ => throw new InvalidOperationException("Steward was not installed from a .deb or .rpm, or the package path is not absolute"),
        };
        return elevated ? command : [Pkexec, .. command];
    }

    public static string? TerminalCommand(LinuxInstall install, string? packageFile = null)
    {
        ArgumentNullException.ThrowIfNull(install);
        return (install.Kind, install.FromFeed) switch
        {
            (LinuxPackageKind.Deb, true) => $"sudo apt update && sudo apt install --only-upgrade {PackageName}",
            (LinuxPackageKind.Rpm, true) => $"sudo dnf upgrade --refresh {PackageName}",
            (LinuxPackageKind.Deb, false) when packageFile is not null => $"sudo apt install ./{packageFile}",
            (LinuxPackageKind.Rpm, false) when packageFile is not null => $"sudo dnf install ./{packageFile}",
            _ => null,
        };
    }

    public static IReadOnlyList<string>? InstalledVersionArguments(LinuxPackageKind kind) => kind switch
    {
        LinuxPackageKind.Deb => ["dpkg-query", "-W", "-f=${Version}", PackageName],
        LinuxPackageKind.Rpm => ["rpm", "-q", "--queryformat", "%{VERSION}", PackageName],
        _ => null,
    };

    public static PackageInstallOutcome Classify(CommandResult result, bool elevated)
    {
        ArgumentNullException.ThrowIfNull(result);
        return (result.ExitCode, elevated) switch
        {
            (0, _) => PackageInstallOutcome.Installed,
            (126, false) => PackageInstallOutcome.Dismissed,
            (127, false) when result.Output.Contains("authentication agent", StringComparison.OrdinalIgnoreCase) => PackageInstallOutcome.NoAuthenticationAgent,
            (127, false) when MissingProgram(result.Output) => PackageInstallOutcome.Failed,
            (127, false) => PackageInstallOutcome.NotAuthorized,
            _ => PackageInstallOutcome.Failed,
        };
    }

    // pkexec exits 127 for "Cannot run program X: No such file or directory" as well as for "Not authorized".
    private static bool MissingProgram(string output) =>
        output.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase)
        || output.Contains(": not found", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> RelaunchArguments(int processId, string exePath, bool hasSetsid) =>
        hasSetsid
            ? ["setsid", "-f", "/bin/sh", "-c", RelaunchScript, "steward-relaunch", processId.ToString(System.Globalization.CultureInfo.InvariantCulture), exePath]
            : ["/bin/sh", "-c", RelaunchScript, "steward-relaunch", processId.ToString(System.Globalization.CultureInfo.InvariantCulture), exePath];

    public static string? FindOnPath(string name, string? path)
    {
        foreach (var directory in (path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
