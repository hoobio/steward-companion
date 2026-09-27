using System.Runtime.CompilerServices;

namespace Steward.Core;

public readonly record struct SseFrame(string? EventType, string? Data);

public static class ServerSentEventReader
{
    public static async IAsyncEnumerable<string> ReadEventsAsync(
        TextReader reader, TimeSpan idleTimeout, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var frame in ReadFramesAsync(reader, idleTimeout, cancellationToken).ConfigureAwait(false))
        {
            yield return frame.EventType ?? "message";
        }
    }

    public static async IAsyncEnumerable<SseFrame> ReadFramesAsync(
        TextReader reader, TimeSpan idleTimeout, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        string? eventType = null;
        string? data = null;
        while (true)
        {
            idle.CancelAfter(idleTimeout);
            string? line;
            try
            {
                line = await reader.ReadLineAsync(idle.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"No event stream traffic for {idleTimeout.TotalSeconds:0} seconds.");
            }

            if (line is null)
            {
                yield break;
            }

            if (line.Length == 0)
            {
                // Dispatched on an event field alone as well as on data, so an `event: ready` sent without a data line still arrives.
                if (eventType is not null || data is not null)
                {
                    yield return new SseFrame(eventType, data);
                }

                eventType = null;
                data = null;
                continue;
            }

            if (line[0] == ':')
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? string.Empty : line[(colon + 1)..];
            value = value.StartsWith(' ') ? value[1..] : value;
            if (field == "event")
            {
                eventType = value.Length == 0 ? null : value;
            }
            else if (field == "data")
            {
                data = data is null ? value : $"{data}\n{value}";
            }
        }
    }
}
