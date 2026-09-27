namespace Steward.App.ViewModels;

internal sealed class SavedVariablesWatcher : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(1500);

    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;

    private SavedVariablesWatcher(FileSystemWatcher watcher, Action changed)
    {
        _watcher = watcher;
        _timer = new Timer(_ => changed(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _watcher.Changed += OnEvent;
        _watcher.Created += OnEvent;
        _watcher.Renamed += OnEvent;
        _watcher.EnableRaisingEvents = true;
    }

    public static SavedVariablesWatcher? TryCreate(string flavourPath, string fileName, Action changed)
    {
        var accountRoot = Path.Combine(flavourPath, "WTF", "Account");
        if (!Directory.Exists(accountRoot))
        {
            return null;
        }

        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(accountRoot, fileName)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            return new SavedVariablesWatcher(watcher, changed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            watcher?.Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _timer.Dispose();
    }

    private void OnEvent(object sender, FileSystemEventArgs e) =>
        _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
}
