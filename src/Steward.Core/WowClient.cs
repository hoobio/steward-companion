using System.ComponentModel;
using System.Diagnostics;

namespace Steward.Core;

public static class WowClient
{
    public static bool IsRunning(WowInstall install) => Find(install) is not null;

    public static WowClientProcess? Find(WowInstall install)
    {
        ArgumentNullException.ThrowIfNull(install);

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
                    if (process.MainModule?.FileName is { } fileName && IsUnder(fileName, install.FlavourPath))
                    {
                        return new WowClientProcess(process.Id, process.StartTime);
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

        return null;
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
