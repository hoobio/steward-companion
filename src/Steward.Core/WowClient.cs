using System.ComponentModel;
using System.Diagnostics;

namespace Steward.Core;

public static class WowClient
{
    public static bool IsRunning(WowInstall install)
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
                        return true;
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

        return false;
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
