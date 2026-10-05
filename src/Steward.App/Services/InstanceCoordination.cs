using System.IO.Pipes;

using Steward.App.Views;
using Steward.Core.Diagnostics;

namespace Steward.App.Services;

public sealed class InstanceCoordination : IDisposable
{
    private const string GlobalMutexName = "Local\\Steward.App";
    private const string QuitEventName = "Local\\Steward.App.Quit";
    private const int MaxLinkLength = 2048;
    private static readonly string[] Trains = ["store", "msi", "dev", "debug"];

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

    public string? Link { get; private init; }

    public static bool TryAcquire(string? link, out InstanceCoordination? coordination)
    {
        var ownTrain = App.Train;
        if (EventWaitHandle.TryOpenExisting(RunningEventName(ownTrain), out var sameTrain))
        {
            sameTrain.Dispose();
            if (link is not null)
            {
                SendLink(ownTrain, link);
            }

            using var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName(ownTrain));
            show.Set();
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
            closedTrain is null ? null : App.TrainDisplayName(closedTrain))
        {
            Link = link,
        };
        return true;
    }

    public void ListenForSignals(MainWindow window)
    {
        var dispatcher = window.DispatcherQueue;
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _show, (_, _) => dispatcher.TryEnqueue(window.ShowFromTray), null, Timeout.Infinite, executeOnlyOnce: false);
        _quitWait = ThreadPool.RegisterWaitForSingleObject(
            _quit, (_, _) => dispatcher.TryEnqueue(() => _ = window.QuitFromAnotherBuildAsync()), null, Timeout.Infinite, executeOnlyOnce: false);
        _ = Task.Run(() => ListenForLinksAsync(link => dispatcher.TryEnqueue(() => _ = window.ViewModel.ReceiveCurseForgeLinkAsync(link))));
    }

    private static async Task ListenForLinksAsync(Action<string> onLink)
    {
        while (true)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    LinkPipeName(App.Train), PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync().ConfigureAwait(false);
                using var reader = new StreamReader(server);
                if (await reader.ReadLineAsync().ConfigureAwait(false) is { Length: > 0 and <= MaxLinkLength } link)
                {
                    onLink(link);
                }
            }
            catch (IOException ex)
            {
                Log(App.Train, ex, "CurseForge link pipe failed; listening again");
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

    private static void LogTimeout(string ownTrain, string? otherTrain) =>
        Log(ownTrain, null, $"Gave up waiting 15s for the {otherTrain ?? "other"} build to quit; exiting without starting");

    private static void Log(string ownTrain, Exception? exception, string message)
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steward", "logs");
        using var provider = new FileLoggerProvider(logDirectory, ownTrain);
        provider.CreateLogger("Steward.App.Program").Warn(exception, message);
    }

    private static string RunningEventName(string train) => $"Local\\Steward.App.Running.{train}";

    private static string LinkPipeName(string train) => $"Steward.App.Link.{train}";

    private static string ShowEventName(string train) => $"Local\\Steward.App.Show.{train}";

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
