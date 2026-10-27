using System.Net.Sockets;

using Steward.Core;

using InstanceSocket = Steward.App.Services.InstanceCoordination.InstanceSocket;

namespace Steward.Desktop.Tests;

public sealed class InstanceSocketTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);
    private readonly string _directory = Directory.CreateTempSubdirectory("sti").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Messages_arrive_in_order_one_per_line()
    {
        var path = InstanceSocket.PathFor(_directory, "debug");
        using var listener = InstanceSocket.Listen(path);
        var received = new List<string>();
        var done = new TaskCompletionSource();
        _ = InstanceSocket.AcceptAsync(listener, message =>
        {
            lock (received)
            {
                received.Add(message);
                if (received.Count == 2)
                {
                    done.TrySetResult();
                }
            }
        }, _ => { }, Patience);

        Assert.True(InstanceSocket.TrySend(path, InstanceSocket.LinkPrefix + "curseforge://install?addonId=1&fileId=2", InstanceSocket.Show));

        await done.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(["link curseforge://install?addonId=1&fileId=2", "show"], received);
    }

    [Theory]
    [InlineData("show", "show")]
    [InlineData("quit", "quit")]
    [InlineData("link curseforge://x", "link:curseforge://x")]
    [InlineData("link ", null)]
    [InlineData("SHOW", null)]
    [InlineData("", null)]
    [InlineData("reload", null)]
    public void Dispatch_routes_known_messages_and_drops_the_rest(string message, string? expected)
    {
        string? seen = null;
        InstanceSocket.Dispatch(message, () => seen = "show", () => seen = "quit", link => seen = "link:" + link);
        Assert.Equal(expected, seen);
    }

    [Fact]
    public void Dispatch_drops_a_link_over_the_length_limit()
    {
        string? seen = null;
        InstanceSocket.Dispatch(InstanceSocket.LinkPrefix + new string('a', AddonSettingsPayload.MaxLinkLength + 1), () => { }, () => { }, link => seen = link);
        Assert.Null(seen);

        InstanceSocket.Dispatch(InstanceSocket.LinkPrefix + new string('a', AddonSettingsPayload.MaxLinkLength), () => { }, () => { }, link => seen = link);
        Assert.Equal(AddonSettingsPayload.MaxLinkLength, seen?.Length);
    }

    [Fact]
    public void Nothing_answers_at_a_missing_path()
    {
        Assert.False(InstanceSocket.TrySend(InstanceSocket.PathFor(_directory, "debug"), InstanceSocket.Show));
    }

    [Fact]
    public void A_stale_socket_file_reads_as_not_running_and_is_replaced_on_listen()
    {
        var path = InstanceSocket.PathFor(_directory, "debug");
        StaleSocket(path);
        Assert.True(File.Exists(path));

        Assert.False(InstanceSocket.TrySend(path, InstanceSocket.Show));

        using var listener = InstanceSocket.Listen(path);
        Assert.True(InstanceSocket.TrySend(path, InstanceSocket.Show));
    }

    [Fact]
    public async Task Quit_goes_to_a_live_other_train_and_skips_its_own_and_dead_ones()
    {
        StaleSocket(InstanceSocket.PathFor(_directory, "macos"));
        using var own = InstanceSocket.Listen(InstanceSocket.PathFor(_directory, "debug"));
        using var other = InstanceSocket.Listen(InstanceSocket.PathFor(_directory, "linux"));
        var quit = new TaskCompletionSource<string>();
        _ = InstanceSocket.AcceptAsync(own, message => quit.TrySetResult("own:" + message), _ => { }, Patience);
        _ = InstanceSocket.AcceptAsync(other, message => quit.TrySetResult(message), _ => { }, Patience);

        Assert.Equal("linux", InstanceSocket.QuitOtherTrain(_directory, "debug"));
        Assert.Equal(InstanceSocket.Quit, await quit.Task.WaitAsync(Patience, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void No_other_train_answers_quit_when_none_is_live()
    {
        StaleSocket(InstanceSocket.PathFor(_directory, "linux"));
        Assert.Null(InstanceSocket.QuitOtherTrain(_directory, "debug"));
    }

    [Fact]
    public async Task A_connection_that_sends_nothing_times_out()
    {
        var path = InstanceSocket.PathFor(_directory, "debug");
        using var listener = InstanceSocket.Listen(path);
        var failed = new TaskCompletionSource<Exception>();
        _ = InstanceSocket.AcceptAsync(listener, _ => { }, ex => failed.TrySetResult(ex), TimeSpan.FromMilliseconds(50));

        using var idle = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await idle.ConnectAsync(new UnixDomainSocketEndPoint(path), TestContext.Current.CancellationToken);

        Assert.IsAssignableFrom<OperationCanceledException>(await failed.Task.WaitAsync(Patience, TestContext.Current.CancellationToken));
    }

    // Disposing a bound socket unlinks its file, so a killed process's leftover is stood in for by a plain file.
    private static void StaleSocket(string path) => File.WriteAllBytes(path, []);
}
