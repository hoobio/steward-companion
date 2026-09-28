using Steward.App.Views;
using Steward.Core.Diagnostics;

namespace Steward.App.Services;

public sealed class InstanceCoordination : IDisposable
{
    private const string GlobalMutexName = "Local\\Steward.App";
    private const string QuitEventName = "Local\\Steward.App.Quit";
    private static readonly string[] Trains = ["store", "msi", "dev", "debug"];

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _running;
    private readonly EventWaitHandle _show;
    private readonly EventWaitHandle _quit;

    private InstanceCoordination(Mutex mutex, EventWaitHandle running, EventWaitHandle show, EventWaitHandle quit, string? closedTrainDisplayName)
    {
        _mutex = mutex;
        _running = running;
        _show = show;
        _quit = quit;
        ClosedTrainDisplayName = closedTrainDisplayName;
    }

    public string? ClosedTrainDisplayName { get; }

    public static bool TryAcquire(out InstanceCoordination? coordination)
    {
        var ownTrain = App.Train;
        if (EventWaitHandle.TryOpenExisting(RunningEventName(ownTrain), out var sameTrain))
        {
            sameTrain.Dispose();
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
            closedTrain is null ? null : App.TrainDisplayName(closedTrain));
        return true;
    }

    public void ListenForSignals(MainWindow window)
    {
        var dispatcher = window.DispatcherQueue;
        _ = Task.Run(() => WaitLoop(_show, () => dispatcher.TryEnqueue(window.ShowFromTray)));
        _ = Task.Run(() => WaitLoop(_quit, () => dispatcher.TryEnqueue(() => _ = window.QuitFromAnotherBuildAsync())));
    }

    private static void WaitLoop(EventWaitHandle handle, Action onSignal)
    {
        while (true)
        {
            handle.WaitOne();
            onSignal();
        }
    }

    private static void LogTimeout(string ownTrain, string? otherTrain)
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steward", "logs");
        using var provider = new FileLoggerProvider(logDirectory, ownTrain);
        provider.CreateLogger("Steward.App.Program").Warn(
            null, $"Gave up waiting 15s for the {otherTrain ?? "other"} build to quit; exiting without starting");
    }

    private static string RunningEventName(string train) => $"Local\\Steward.App.Running.{train}";

    private static string ShowEventName(string train) => $"Local\\Steward.App.Show.{train}";

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _running.Dispose();
        _show.Dispose();
        _quit.Dispose();
    }
}
