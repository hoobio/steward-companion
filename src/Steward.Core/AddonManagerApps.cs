using System.Diagnostics;

using Microsoft.Win32;

namespace Steward.Core;

public sealed record AddonManagerApp(string Name, bool StartsWithWindows, bool IsRunning);

public static class AddonManagerApps
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    // ponytail: matched by exe filename since the Run value names are undocumented; CurseForge hosted inside Overwolf is only caught while running
    private static readonly string[] Names = ["CurseForge", "WowUp", "WowUp-CF"];

    public static IReadOnlyList<AddonManagerApp> Detect()
    {
        var startup = StartupTargets().Where(File.Exists).Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. Names
            .Select(name => new AddonManagerApp(name, startup.Contains(name), IsRunning(name)))
            .Where(app => app.StartsWithWindows || app.IsRunning)];
    }

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
