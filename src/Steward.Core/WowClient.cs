using System.ComponentModel;
using System.Diagnostics;

namespace Steward.Core;

public static class WowClient
{
    public static bool IsRunning(WowInstall install) => Find(install) is not null;

    public static WowClientProcess? Find(WowInstall install) => Find(install, Snapshot());

    public static WowClientProcess? Find(WowInstall install, IReadOnlyList<(string FileName, WowClientProcess Process)> snapshot)
    {
        ArgumentNullException.ThrowIfNull(install);
        return Find(install.FlavourPath, snapshot);
    }

    public static WowClientProcess? Find(string flavourPath, IReadOnlyList<(string FileName, WowClientProcess Process)> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.FirstOrDefault(entry => IsUnder(entry.FileName, flavourPath)).Process;
    }

    public static IReadOnlyList<(string FileName, WowClientProcess Process)> Snapshot()
    {
        if (OperatingSystem.IsLinux())
        {
            return [.. WineClients(ProcFs.Root, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
                .Select(client => (client.FileName, Process: Started(client.ProcessId)))
                .Where(client => client.Process is not null)
                .Select(client => (client.FileName, client.Process!))];
        }

        var clients = new List<(string, WowClientProcess)>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                // MainModule throws for elevated processes, so only Wow* candidates are opened.
                if (!process.ProcessName.StartsWith("Wow", StringComparison.OrdinalIgnoreCase)
                    && !(OperatingSystem.IsMacOS() && process.ProcessName.StartsWith("World of Warcraft", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                try
                {
                    if (process.MainModule?.FileName is { } fileName)
                    {
                        clients.Add((fileName, new WowClientProcess(process.Id, process.StartTime)));
                    }
                }
                catch (Win32Exception)
                {
                }
                catch (InvalidOperationException)
                {
                }
                catch (NotSupportedException)
                {
                }
            }
        }

        return clients;
    }

    public static async Task WaitForExitAsync(int processId, CancellationToken cancellationToken)
    {
        Process? process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (process)
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal static IEnumerable<(string FileName, int ProcessId)> WineClients(string procRoot, string home)
    {
        foreach (var process in ProcFs.Processes(procRoot))
        {
            if (process.Arguments.FirstOrDefault(IsClientExe) is { } exe && ClientPath(process, exe, home) is { } fileName)
            {
                yield return (fileName, process.ProcessId);
            }
        }
    }

    private static bool IsClientExe(string argument)
    {
        var name = ProcFs.FileName(argument);
        return name.StartsWith("Wow", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    // Under Wine the process is the wine preloader, so the client's Windows path is only in its arguments.
    private static string? ClientPath(ProcEntry process, string exe, string home)
    {
        if (exe.StartsWith('/'))
        {
            return exe;
        }

        var environment = process.Environment();
        var prefix = environment.GetValueOrDefault("WINEPREFIX")
            ?? (environment.GetValueOrDefault("STEAM_COMPAT_DATA_PATH") is { } steam ? Path.Combine(steam, "pfx") : null)
            ?? Path.Combine(environment.GetValueOrDefault("HOME") ?? home, ".wine");
        return WinePrefixes.ToUnixPath(exe, prefix)
            ?? (process.WorkingDirectory() is { } directory ? Path.Combine(directory, ProcFs.FileName(exe)) : null);
    }

    private static WowClientProcess? Started(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return new WowClientProcess(processId, process.StartTime);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    internal static bool IsUnder(string filePath, string folderPath)
    {
        var file = Path.GetFullPath(filePath);
        var folder = Path.GetFullPath(folderPath);
        if (!Path.EndsInDirectorySeparator(folder))
        {
            folder += Path.DirectorySeparatorChar;
        }

        return file.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
    }
}
