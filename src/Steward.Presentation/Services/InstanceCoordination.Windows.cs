using System.IO.Pipes;

namespace Steward.App.Services;

public sealed partial class InstanceCoordination : IDisposable
{
    private const string GlobalMutexName = "Local\\Steward.App";
    private const string QuitEventName = "Local\\Steward.App.Quit";
    // The -avalonia trains are the desktop previews from before the cutover, still closed when a current build starts.
    private static readonly string[] Trains = ["store", "msi", "preview", "dev", "debug", "store-avalonia", "msi-avalonia", "dev-avalonia", "debug-avalonia"];

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _running;
    private readonly EventWaitHandle _show;
    private readonly EventWaitHandle _quit;
    private RegisteredWaitHandle? _showWait;
    private RegisteredWaitHandle? _quitWait;

    private InstanceCoordination(Mutex mutex, EventWaitHandle running, EventWaitHandle show, EventWaitHandle quit, string? closedTrainDisplayName)
    {
        _mutex = mutex;
        _running = running;
        _show = show;
        _quit = quit;
        ClosedTrainDisplayName = closedTrainDisplayName;
    }

    public string? ClosedTrainDisplayName { get; }

    public static bool TryAcquire(string? link, out InstanceCoordination? coordination, bool showRunning = true)
    {
        var ownTrain = AppInfo.Train;
        if (EventWaitHandle.TryOpenExisting(RunningEventName(ownTrain), out var sameTrain))
        {
            sameTrain.Dispose();
            if (link is not null)
            {
                SendLink(ownTrain, link);
            }

            if (showRunning)
            {
                using var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName(ownTrain));
                show.Set();
            }

            coordination = null;
            return false;
        }

        string? closedTrain = null;
        foreach (var train in Trains)
        {
            if (train == ownTrain)
            {
                continue;
            }

            if (EventWaitHandle.TryOpenExisting(RunningEventName(train), out var other))
            {
                other.Dispose();
                closedTrain = train;
                break;
            }
        }

        var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        if (closedTrain is not null)
        {
            quit.Set();
        }

        var mutex = new Mutex(initiallyOwned: false, GlobalMutexName);
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(TimeSpan.FromSeconds(15));
        }
        // A Store update force-kills the old process without releasing the mutex; the relaunch must still proceed.
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            LogTimeout(ownTrain, closedTrain);
            mutex.Dispose();
            quit.Dispose();
            coordination = null;
            return false;
        }

        var running = new EventWaitHandle(false, EventResetMode.ManualReset, RunningEventName(ownTrain));
        var show2 = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName(ownTrain));
        coordination = new InstanceCoordination(
            mutex, running, show2, quit,
            closedTrain is null ? null : AppInfo.TrainDisplayName(closedTrain))
        {
            Link = link,
        };
        return true;
    }

    public void ListenForSignals(IUiPlatform ui, Action show, Func<Task> quit, Func<string, Task> receiveLink)
    {
        ArgumentNullException.ThrowIfNull(ui);

        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _show, (_, _) => ui.TryEnqueue(show), null, Timeout.Infinite, executeOnlyOnce: false);
        _quitWait = ThreadPool.RegisterWaitForSingleObject(
            _quit, (_, _) => ui.TryEnqueue(() => _ = quit()), null, Timeout.Infinite, executeOnlyOnce: false);
        _ = Task.Run(() => ListenForLinksAsync(link => ui.TryEnqueue(() => _ = receiveLink(link))));
    }

    private static void LogTimeout(string ownTrain, string? otherTrain) =>
        Log(ownTrain, null, $"Gave up waiting 15s for the {otherTrain ?? "other"} build to quit; exiting without starting");

    private static string RunningEventName(string train) => $"Local\\Steward.App.Running.{train}";

    private static string ShowEventName(string train) => $"Local\\Steward.App.Show.{train}";

    private static async Task ListenForLinksAsync(Action<string> onLink)
    {
        while (true)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    LinkPipeName(AppInfo.Train), PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync().ConfigureAwait(false);
                using var reader = new StreamReader(server);
                if (await reader.ReadLineAsync().ConfigureAwait(false) is { Length: > 0 and <= MaxLinkLength } link)
                {
                    onLink(link);
                }
            }
            catch (IOException ex)
            {
                Log(AppInfo.Train, ex, "CurseForge link pipe failed; listening again");
            }
        }
    }

    private static void SendLink(string train, string link)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", LinkPipeName(train), PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(TimeSpan.FromSeconds(5));
            using var writer = new StreamWriter(client);
            writer.WriteLine(link);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            Log(train, ex, "Could not hand a CurseForge link to the running instance");
        }
    }

    private static string LinkPipeName(string train) => $"Steward.App.Link.{train}";

    public void Dispose()
    {
        _showWait?.Unregister(null);
        _quitWait?.Unregister(null);
        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _running.Dispose();
        _show.Dispose();
        _quit.Dispose();
    }
}
