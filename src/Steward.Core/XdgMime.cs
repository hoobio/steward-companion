namespace Steward.Core;

public delegate (int ExitCode, string Output) ProcessRunner(string fileName, IReadOnlyList<string> arguments);

public static class XdgMime
{
    public const string DesktopFile = "steward.desktop";
    public const string CurseForgeMimeType = "x-scheme-handler/curseforge";

    public static IReadOnlyList<string> QueryArguments { get; } = ["query", "default", CurseForgeMimeType];

    public static IReadOnlyList<string> SetDefaultArguments { get; } = ["default", DesktopFile, CurseForgeMimeType];

    public static bool IsDefault(ProcessRunner run)
    {
        var (exitCode, output) = run("xdg-mime", QueryArguments);
        return exitCode == 0 && string.Equals(output.Trim(), DesktopFile, StringComparison.Ordinal);
    }

    public static bool SetDefault(ProcessRunner run) => run("xdg-mime", SetDefaultArguments).ExitCode == 0;

    public static IEnumerable<string> ApplicationDirs(string? dataHome, string? dataDirs, string home)
    {
        yield return Path.Combine(Path.IsPathRooted(dataHome) ? dataHome : Path.Combine(home, ".local", "share"), "applications");
        foreach (var dir in (string.IsNullOrEmpty(dataDirs) ? "/usr/local/share:/usr/share" : dataDirs).Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return Path.Combine(dir, "applications");
        }
    }

    public static bool IsOnPath(string? path, string fileName) =>
        (path ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries).Any(dir => File.Exists(Path.Combine(dir, fileName)));
}
