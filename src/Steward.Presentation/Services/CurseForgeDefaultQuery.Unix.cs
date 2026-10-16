using System.Diagnostics;

using Steward.Core;

namespace Steward.App.Services;

public static class CurseForgeDefaultQuery
{
    private static readonly Lazy<bool> Available = new(() => OperatingSystem.IsLinux()
        && XdgMime.IsOnPath(Environment.GetEnvironmentVariable("PATH"), "xdg-mime")
        && XdgMime.ApplicationDirs(
                Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
                Environment.GetEnvironmentVariable("XDG_DATA_DIRS"),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
            .Any(dir => File.Exists(Path.Combine(dir, XdgMime.DesktopFile))));

    public static string? Aumid => Available.Value ? XdgMime.DesktopFile : null;

    public static bool IsDefault(string aumid) => XdgMime.IsDefault(Run);

    public static bool HasAnyDefault() => false;

    public static bool TrySetDefault(string aumid) => XdgMime.SetDefault(Run);

    private static (int ExitCode, string Output) Run(string fileName, IReadOnlyList<string> arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments) { RedirectStandardOutput = true, UseShellExecute = false })
            ?? throw new InvalidOperationException($"Could not start {fileName}");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
