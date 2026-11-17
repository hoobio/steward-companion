using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Steward.Core;

public sealed record RecycleOutcome(IReadOnlyList<string> Moved, IReadOnlyList<string> Left, string? Error)
{
    public string? Failure => Left.Count == 0
        ? null
        : Moved.Count == 0
            ? $"Uninstall failed, nothing was moved: {Error}"
            : $"Uninstall partly failed: {string.Join(", ", Moved)} moved to {AddonRecycler.Destination}, {string.Join(", ", Left)} still in the AddOns folder: {Error}";
}

public static class AddonRecycler
{
    private const uint NoUiAllowUndo = 0x614 | 0x40;
    private const uint RecycleOnDelete = 0x00080000;
    private const uint RecycleIfPossible = 0x80;
    private const int Aborted = unchecked((int)0x80004004);

    public static string Destination => OperatingSystem.IsWindows() ? "the Recycle Bin" : "Steward's removed addons folder";

    public static string RemovedFolder => Path.Combine(LocalData.Folder, "Removed addons");

    public static async Task RecycleAsync(string addOnsPath, string folder)
    {
        if (AddonUpdater.RemovalRefusal(addOnsPath, folder) is { } refusal)
        {
            throw new IOException(refusal);
        }

        var path = Path.Combine(addOnsPath, folder);
        if (AddonUpdater.IsLink(path))
        {
            throw new IOException($"{path} is a link, so it was left in place");
        }

        if (!Directory.Exists(path))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            await RecycleOnWindowsAsync(path).ConfigureAwait(false);
        }
        else
        {
            var dated = Directory.CreateDirectory(Path.Combine(RemovedFolder, DateTime.Now.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture))).FullName;
            MoveFolder(path, Path.Combine(dated, folder));
        }

        if (Directory.Exists(path))
        {
            throw new IOException($"{path} was not moved to {Destination}, so it was left in place");
        }
    }

    public static Task<RecycleOutcome> RecycleFoldersAsync(string addOnsPath, IReadOnlyList<string> folders, Func<bool> wowRunning, Func<string, string, Task>? recycle = null)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(wowRunning);
        return Task.Run(() =>
        {
            var moved = new List<string>();
            var left = new List<string>();
            string? error = null;
            lock (AddonUpdater.AddOnsWriteLock)
            {
                if (wowRunning())
                {
                    return new RecycleOutcome([], folders, "World of Warcraft is running from this install. Close it and try again.");
                }

                foreach (var folder in folders)
                {
                    try
                    {
                        (recycle ?? RecycleAsync)(addOnsPath, folder).GetAwaiter().GetResult();
                        moved.Add(folder);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        left.Add(folder);
                        error ??= ex.Message;
                    }
                }
            }

            return new RecycleOutcome(moved, left, error);
        });
    }

    internal static void MoveFolder(string source, string destination, Action<string, string>? move = null)
    {
        try
        {
            (move ?? Directory.Move)(source, destination);
            return;
        }
        catch (IOException) when (Directory.Exists(source) && !Directory.Exists(destination))
        {
        }

        var walk = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var directory in Directory.EnumerateDirectories(source, "*", walk))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", walk))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
        }

        if (Measure(source, walk) != Measure(destination, walk))
        {
            Directory.Delete(destination, recursive: true);
            throw new IOException($"the copy of {source} did not match it, so it was left in place");
        }

        Directory.Delete(source, recursive: true);
    }

    private static (int Files, long Bytes) Measure(string folder, EnumerationOptions walk)
    {
        var files = new DirectoryInfo(folder).GetFiles("*", walk);
        return (files.Length, files.Sum(file => file.Length));
    }

    [SupportedOSPlatform("windows")]
    private static Task RecycleOnWindowsAsync(string path)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                SendToRecycleBin(path);
                done.SetResult();
            }
            catch (Exception ex)
            {
                done.SetException(new IOException($"Could not move {path} to the Recycle Bin, so it was left in place: {ex.Message}", ex));
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    [SupportedOSPlatform("windows")]
    private static void SendToRecycleBin(string path)
    {
        var operation = (IFileOperation)new FileOperation();
        IShellItem? item = null;
        try
        {
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItem).GUID, out item));
            Marshal.ThrowExceptionForHR(operation.SetOperationFlags(NoUiAllowUndo | RecycleOnDelete));
            Marshal.ThrowExceptionForHR(operation.DeleteItem(item, new RecycleOnlySink()));
            var result = operation.PerformOperations();
            Marshal.ThrowExceptionForHR(operation.GetAnyOperationsAborted(out var aborted));
            if (result < 0 || aborted)
            {
                throw new IOException(result < 0 ? $"the Recycle Bin could not take it (0x{result:X8})" : "the Recycle Bin could not take it");
            }
        }
        finally
        {
            if (item is not null)
            {
                Marshal.ReleaseComObject(item);
            }

            Marshal.ReleaseComObject(operation);
        }
    }

    // Without the recycle flag the shell is about to delete the item permanently (volume without a bin, or too large for it).
    internal static int PreDeleteResult(uint flags) => (flags & RecycleIfPossible) != 0 ? 0 : Aborted;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, [In] in Guid riid, out IShellItem item);

    [ComImport, Guid("3ad05575-8857-4850-9277-11b85bdb8e09")]
    private class FileOperation;

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem;

    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        [PreserveSig] int Advise(IFileOperationProgressSink sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage(IntPtr message);
        [PreserveSig] int SetProgressDialog(IntPtr dialog);
        [PreserveSig] int SetProperties(IntPtr properties);
        [PreserveSig] int SetOwnerWindow(IntPtr owner);
        [PreserveSig] int ApplyPropertiesToItem(IntPtr item);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
        [PreserveSig] int RenameItem(IntPtr item, IntPtr newName, IntPtr sink);
        [PreserveSig] int RenameItems(IntPtr items, IntPtr newName);
        [PreserveSig] int MoveItem(IntPtr item, IntPtr destination, IntPtr newName, IntPtr sink);
        [PreserveSig] int MoveItems(IntPtr items, IntPtr destination);
        [PreserveSig] int CopyItem(IntPtr item, IntPtr destination, IntPtr copyName, IntPtr sink);
        [PreserveSig] int CopyItems(IntPtr items, IntPtr destination);
        [PreserveSig] int DeleteItem(IShellItem item, IFileOperationProgressSink sink);
        [PreserveSig] int DeleteItems(IntPtr items);
        [PreserveSig] int NewItem(IntPtr destination, uint attributes, IntPtr name, IntPtr template, IntPtr sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [ComImport, Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, IntPtr item, IntPtr newName);
        [PreserveSig] int PostRenameItem(uint flags, IntPtr item, IntPtr newName, int result, IntPtr newItem);
        [PreserveSig] int PreMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr newName);
        [PreserveSig] int PostMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr newName, int result, IntPtr newItem);
        [PreserveSig] int PreCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr newName);
        [PreserveSig] int PostCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr newName, int result, IntPtr newItem);
        [PreserveSig] int PreDeleteItem(uint flags, IntPtr item);
        [PreserveSig] int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr newItem);
        [PreserveSig] int PreNewItem(uint flags, IntPtr destination, IntPtr newName);
        [PreserveSig] int PostNewItem(uint flags, IntPtr destination, IntPtr newName, IntPtr template, uint attributes, int result, IntPtr newItem);
        [PreserveSig] int UpdateProgress(uint total, uint done);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    [ComVisible(true)]
    private sealed class RecycleOnlySink : IFileOperationProgressSink
    {
        public int StartOperations() => 0;
        public int FinishOperations(int result) => 0;
        public int PreRenameItem(uint flags, IntPtr item, IntPtr newName) => 0;
        public int PostRenameItem(uint flags, IntPtr item, IntPtr newName, int result, IntPtr newItem) => 0;
        public int PreMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr newName) => 0;
        public int PostMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr newName, int result, IntPtr newItem) => 0;
        public int PreCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr newName) => 0;
        public int PostCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr newName, int result, IntPtr newItem) => 0;
        public int PreDeleteItem(uint flags, IntPtr item) => PreDeleteResult(flags);
        public int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr newItem) => 0;
        public int PreNewItem(uint flags, IntPtr destination, IntPtr newName) => 0;
        public int PostNewItem(uint flags, IntPtr destination, IntPtr newName, IntPtr template, uint attributes, int result, IntPtr newItem) => 0;
        public int UpdateProgress(uint total, uint done) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }
}
