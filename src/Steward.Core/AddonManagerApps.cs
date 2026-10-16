using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

using Microsoft.Win32;

namespace Steward.Core;

public sealed record AddonManagerApp(string Name, bool StartsWithWindows, bool IsRunning);

public static partial class AddonManagerApps
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    // ponytail: matched by exe filename since the Run value names are undocumented; CurseForge hosted inside Overwolf is only caught while running
    private static readonly string[] Names = ["CurseForge", "WowUp", "WowUp-CF"];

    public static IReadOnlyList<AddonManagerApp> Detect()
    {
        if (OperatingSystem.IsLinux())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } configHome ? configHome : Path.Combine(home, ".config");
            return DetectLinux(Path.Combine(config, "autostart"), ProcFs.Root);
        }

        var startup = (OperatingSystem.IsWindows() ? StartupTargets() : []).Where(File.Exists).Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. Names
            .Select(name => new AddonManagerApp(name, startup.Contains(name), IsRunning(name)))
            .Where(app => app.StartsWithWindows || app.IsRunning)];
    }

    internal static IReadOnlyList<AddonManagerApp> DetectLinux(string autostartFolder, string procRoot)
    {
        var startup = AutostartTargets(autostartFolder).Where(File.Exists).Select(AppKey).ToHashSet(StringComparer.Ordinal);
        var running = ProcFs.Processes(procRoot)
            .SelectMany(process => process.Environment().GetValueOrDefault("APPIMAGE") is { } appImage
                ? process.Arguments.Take(1).Append(appImage)
                : process.Arguments.Take(1))
            .Select(AppKey)
            .ToHashSet(StringComparer.Ordinal);
        return [.. Names
            .Select(name => new AddonManagerApp(name, startup.Contains(AppKey(name)), running.Contains(AppKey(name))))
            .Where(app => app.StartsWithWindows || app.IsRunning)];
    }

    internal static IEnumerable<string> AutostartTargets(string autostartFolder)
    {
        if (!Directory.Exists(autostartFolder))
        {
            yield break;
        }

        foreach (var entry in Directory.EnumerateFiles(autostartFolder, "*.desktop"))
        {
            var lines = File.ReadAllLines(entry).Select(line => line.Trim()).ToList();
            if (lines.Any(line => line.Equals("Hidden=true", StringComparison.OrdinalIgnoreCase) || line.Equals("X-GNOME-Autostart-enabled=false", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (lines.FirstOrDefault(line => line.StartsWith("Exec=", StringComparison.Ordinal)) is { } exec && ExePath(exec[5..]) is { } path)
            {
                yield return path;
            }
        }
    }

    internal static string AppKey(string path)
    {
        var name = ProcFs.FileName(path);
        if (name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^".AppImage".Length];
        }

        return AppVersionSuffix().Replace(name, string.Empty).Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
    }

    [GeneratedRegex(@"[-_ ]v?\d[\w.\-]*$")]
    private static partial Regex AppVersionSuffix();

    public static string? ExePath(string? command)
    {
        var trimmed = command?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed[0] == '"')
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : null;
        }

        var exe = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? trimmed[..(exe + 4)] : trimmed.Split(' ')[0];
    }

    public static bool IsApproved(byte[]? value) => value is not { Length: > 0 } || value[0] == 2;

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> StartupTargets()
    {
        foreach (var (hive, run, approved) in new[]
        {
            (Registry.CurrentUser, RunKey, "Run"),
            (Registry.LocalMachine, RunKey, "Run"),
            (Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32"),
        })
        {
            using var runKey = hive.OpenSubKey(run);
            using var approvedKey = hive.OpenSubKey(ApprovedKey + approved);
            foreach (var name in runKey?.GetValueNames() ?? [])
            {
                if (IsApproved(approvedKey?.GetValue(name) as byte[]) && ExePath(runKey!.GetValue(name) as string) is { } path)
                {
                    yield return Environment.ExpandEnvironmentVariables(path);
                }
            }
        }

        foreach (var (folder, hive) in new[]
        {
            (Environment.SpecialFolder.Startup, Registry.CurrentUser),
            (Environment.SpecialFolder.CommonStartup, Registry.LocalMachine),
        })
        {
            var directory = Environment.GetFolderPath(folder);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            using var approvedKey = hive.OpenSubKey(ApprovedKey + "StartupFolder");
            foreach (var link in Directory.EnumerateFiles(directory, "*.lnk"))
            {
                if (IsApproved(approvedKey?.GetValue(Path.GetFileName(link)) as byte[]) && ShortcutTarget(link) is { } target)
                {
                    yield return target;
                }
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? ShortcutTarget(string link)
    {
        if (Type.GetTypeFromProgID("WScript.Shell") is not { } type)
        {
            return null;
        }

        dynamic? shell = Activator.CreateInstance(type);
        try
        {
            return shell?.CreateShortcut(link).TargetPath as string;
        }
        finally
        {
            if (shell is not null)
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static bool IsRunning(string name)
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }
}
