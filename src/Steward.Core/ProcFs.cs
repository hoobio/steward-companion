namespace Steward.Core;

internal sealed record ProcEntry(int ProcessId, string Folder, IReadOnlyList<string> Arguments)
{
    public IReadOnlyDictionary<string, string> Environment()
    {
        try
        {
            return File.ReadAllText(Path.Combine(Folder, "environ"))
                .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(entry => entry.Split('=', 2))
                .Where(pair => pair.Length == 2)
                .GroupBy(pair => pair[0], StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First()[1], StringComparer.Ordinal);
        }
        catch (IOException)
        {
            return new Dictionary<string, string>();
        }
        catch (UnauthorizedAccessException)
        {
            return new Dictionary<string, string>();
        }
    }

    public string? WorkingDirectory()
    {
        try
        {
            return new DirectoryInfo(Path.Combine(Folder, "cwd")).LinkTarget;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

internal static class ProcFs
{
    public const string Root = "/proc";

    public static IEnumerable<ProcEntry> Processes(string procRoot)
    {
        string[] folders;
        try
        {
            folders = Directory.Exists(procRoot) ? Directory.GetDirectories(procRoot) : [];
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var folder in folders)
        {
            if (!int.TryParse(Path.GetFileName(folder), out var processId))
            {
                continue;
            }

            string cmdline;
            try
            {
                cmdline = File.ReadAllText(Path.Combine(folder, "cmdline"));
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            yield return new ProcEntry(processId, folder, cmdline.Split('\0', StringSplitOptions.RemoveEmptyEntries));
        }
    }

    public static string FileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
}
