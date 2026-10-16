using System.Collections.Concurrent;

namespace Steward.Desktop.Tests;

public sealed class OfflineHandler(ConcurrentQueue<Uri> requests) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        requests.Enqueue(request.RequestUri!);
        throw new HttpRequestException("Network access is disabled in tests.");
    }
}
