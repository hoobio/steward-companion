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
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.FirstOrDefault(entry => IsUnder(entry.FileName, install.FlavourPath)).Process;
    }

    public static IReadOnlyList<(string FileName, WowClientProcess Process)> Snapshot()
    {
        var clients = new List<(string, WowClientProcess)>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                // MainModule throws for elevated processes, so only Wow* candidates are opened.
                if (!process.ProcessName.StartsWith("Wow", StringComparison.OrdinalIgnoreCase))
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
