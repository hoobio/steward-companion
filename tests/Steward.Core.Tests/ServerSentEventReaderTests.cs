namespace Steward.Core.Tests;

public sealed class ServerSentEventReaderTests
{
    private static async Task<List<string>> Read(string text)
    {
        var events = new List<string>();
        await foreach (var eventType in ServerSentEventReader.ReadEventsAsync(
            new StringReader(text), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))
        {
            events.Add(eventType);
        }

        return events;
    }

    [Fact]
    public async Task ReadEventsAsync_DispatchesOnBlankLine()
    {
        var events = await Read(
            "event: ready\n\n: ping\n\nevent: roster-changed\ndata: {\"guild\":\"1\"}\n\nevent: members-changed\ndata: {}\n\n");

        Assert.Equal(["ready", "roster-changed", "members-changed"], events);
    }

    [Fact]
    public async Task ReadEventsAsync_HandlesCrLf()
    {
        var events = await Read("event: characters-changed\r\ndata: {\"guild\":\"1\"}\r\n\r\n");

        Assert.Equal(["characters-changed"], events);
    }

    [Fact]
    public async Task ReadEventsAsync_IgnoresCommentsAndBlankLinesAlone()
    {
        var events = await Read(": ping\n\n\n: ping\n\n");

        Assert.Empty(events);
    }

    [Fact]
    public async Task ReadEventsAsync_DataWithoutEventIsMessage()
    {
        var events = await Read("data: one\ndata: two\n\n");

        Assert.Equal(["message"], events);
    }

    [Fact]
    public async Task ReadEventsAsync_MultiLineDataIsOneEvent()
    {
        var events = await Read("event: roster-changed\ndata: {\ndata: \"guild\":\"1\"}\n\n");

        Assert.Equal(["roster-changed"], events);
    }

    [Fact]
    public async Task ReadEventsAsync_DropsEventCutOffBeforeBlankLine()
    {
        var events = await Read("event: ready\n\nevent: roster-changed\ndata: {}\n");

        Assert.Equal(["ready"], events);
    }

    [Fact]
    public async Task ReadEventsAsync_ResetsEventTypeBetweenEvents()
    {
        var events = await Read("event: ready\n\ndata: x\n\n");

        Assert.Equal(["ready", "message"], events);
    }

    [Fact]
    public async Task ReadEventsAsync_ThrowsTimeoutWhenIdle()
    {
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (var _ in ServerSentEventReader.ReadEventsAsync(
                new SilentReader(), TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken))
            {
            }
        });
    }

    private sealed class SilentReader : TextReader
    {
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }
    }

    private static async Task<List<SseFrame>> ReadFrames(string text)
    {
        var frames = new List<SseFrame>();
        await foreach (var frame in ServerSentEventReader.ReadFramesAsync(
            new StringReader(text), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))
        {
            frames.Add(frame);
        }

        return frames;
    }

    [Fact]
    public async Task ReadFramesAsync_CarriesTheDataPayload_WhenThereIsNoEventField()
    {
        var frames = await ReadFrames("data: {\"type\": \"accessChanged\"}\n\n");

        var frame = Assert.Single(frames);
        Assert.Null(frame.EventType);
        Assert.Equal("{\"type\": \"accessChanged\"}", frame.Data);
    }

    [Fact]
    public async Task ReadFramesAsync_CarriesBothTheEventFieldAndTheData()
    {
        var frames = await ReadFrames("event: roster-changed\ndata: {\"guild\":\"1\"}\n\n");

        var frame = Assert.Single(frames);
        Assert.Equal("roster-changed", frame.EventType);
        Assert.Equal("{\"guild\":\"1\"}", frame.Data);
    }
}
