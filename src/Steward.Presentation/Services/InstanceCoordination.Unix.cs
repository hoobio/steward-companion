using System.Net.Sockets;
using System.Text;

using Steward.Core;

namespace Steward.App.Services;

public sealed partial class InstanceCoordination : IDisposable
{
    private const string MutexName = "Steward.App";

    private readonly Mutex _mutex;
    private readonly Socket _listener;
    private readonly string _socketPath;

    private InstanceCoordination(Mutex mutex, Socket listener, string socketPath, string? closedTrainDisplayName)
    {
        _mutex = mutex;
        _listener = listener;
        _socketPath = socketPath;
        ClosedTrainDisplayName = closedTrainDisplayName;
    }

    public string? ClosedTrainDisplayName { get; }

    public static bool TryAcquire(string? link, out InstanceCoordination? coordination, bool showRunning = true)
    {
        var ownTrain = AppInfo.Train;
        var directory = InstanceSocket.Directory();
        var ownPath = InstanceSocket.PathFor(directory, ownTrain);
        var handover = new List<string>();
        if (link is not null)
        {
            handover.Add(InstanceSocket.LinkPrefix + link);
        }

        if (showRunning)
        {
            handover.Add(InstanceSocket.Show);
        }

        if (InstanceSocket.TrySend(ownPath, [.. handover]))
        {
            coordination = null;
            return false;
        }

        var closedTrain = InstanceSocket.QuitOtherTrain(directory, ownTrain);

        // Local\ on Unix is scoped to the login session, so two terminals would each acquire it.
        var mutex = new Mutex(initiallyOwned: false, MutexName, new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = false });
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(TimeSpan.FromSeconds(15));
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            Log(ownTrain, null, $"Gave up waiting 15s for the {closedTrain ?? "other"} build to quit; exiting without starting");
            mutex.Dispose();
            coordination = null;
            return false;
        }

        coordination = new InstanceCoordination(
            mutex, InstanceSocket.Listen(ownPath), ownPath,
            closedTrain is null ? null : AppInfo.TrainDisplayName(closedTrain))
        {
            Link = link,
        };
        return true;
    }

    public void ListenForSignals(IUiPlatform ui, Action show, Func<Task> quit, Func<string, Task> receiveLink)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _ = Task.Run(() => InstanceSocket.AcceptAsync(
            _listener,
            message => InstanceSocket.Dispatch(
                message,
                () => ui.TryEnqueue(show),
                () => ui.TryEnqueue(() => _ = quit()),
                link => ui.TryEnqueue(() => _ = receiveLink(link))),
            ex => Log(AppInfo.Train, ex, "Instance socket connection failed"),
            TimeSpan.FromSeconds(10)));
    }

    public void Dispose()
    {
        _listener.Dispose();
        File.Delete(_socketPath);
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    internal static class InstanceSocket
    {
        internal const string Show = "show";
        internal const string Quit = "quit";
        internal const string LinkPrefix = "link ";
        private const string Extension = ".sock";
        private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        public static string Directory()
        {
            var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            var directory = string.IsNullOrEmpty(runtime)
                ? Path.Combine(LocalData.Folder, "run")
                : Path.Combine(runtime, "steward");
            if (OperatingSystem.IsWindows())
            {
                System.IO.Directory.CreateDirectory(directory);
            }
            else
            {
                System.IO.Directory.CreateDirectory(directory, OwnerOnly);
                File.SetUnixFileMode(directory, OwnerOnly);
            }

            return directory;
        }

        public static string PathFor(string directory, string train) => Path.Combine(directory, train + Extension);

        public static bool TrySend(string path, params string[] messages)
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                socket.Connect(new UnixDomainSocketEndPoint(path));
                socket.Send(Encoding.UTF8.GetBytes(string.Concat(messages.Select(message => message + "\n"))));
                socket.Shutdown(SocketShutdown.Send);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        public static string? QuitOtherTrain(string directory, string ownTrain)
        {
            foreach (var path in System.IO.Directory.EnumerateFiles(directory, "*" + Extension))
            {
                var train = Path.GetFileNameWithoutExtension(path);
                if (train != ownTrain && TrySend(path, Quit))
                {
                    return train;
                }
            }

            return null;
        }

        public static Socket Listen(string path)
        {
            File.Delete(path);
            var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen();
            return listener;
        }

        public static async Task AcceptAsync(Socket listener, Action<string> onMessage, Action<Exception> onError, TimeSpan readTimeout)
        {
            while (true)
            {
                Socket connection;
                try
                {
                    connection = await listener.AcceptAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is ObjectDisposedException or SocketException)
                {
                    return;
                }

                _ = ReadAsync(connection, onMessage, onError, readTimeout);
            }
        }

        private static async Task ReadAsync(Socket connection, Action<string> onMessage, Action<Exception> onError, TimeSpan readTimeout)
        {
            using var timeout = new CancellationTokenSource(readTimeout);
            try
            {
                await using var stream = new NetworkStream(connection, ownsSocket: true);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                while (await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { } line)
                {
                    onMessage(line);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
            {
                onError(ex);
            }
        }

        public static void Dispatch(string message, Action show, Action quit, Action<string> link)
        {
            if (message == Show)
            {
                show();
            }
            else if (message == Quit)
            {
                quit();
            }
            else if (message.StartsWith(LinkPrefix, StringComparison.Ordinal)
                && message.Length - LinkPrefix.Length is > 0 and <= MaxLinkLength)
            {
                link(message[LinkPrefix.Length..]);
            }
        }
    }
}
