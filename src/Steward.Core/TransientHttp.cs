using System.Net;

namespace Steward.Core;

public static class TransientHttp
{
    public static readonly HttpRequestOptionsKey<bool> Idempotent = new("Steward.Idempotent");

    private static readonly TimeSpan[] Schedule = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16)];

    public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    public static TimeSpan Backoff(int attempt) => attempt < Schedule.Length ? Schedule[Math.Max(attempt, 0)] : MaxBackoff;

    public static bool IsTransient(HttpStatusCode? status) =>
        status is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    public static bool IsTransient(Exception ex) => ex switch
    {
        ClientOutdatedException or SessionExpiredException => false,
        HttpRequestException { StatusCode: { } status } => IsTransient(status),
        HttpRequestException http => http.HttpRequestError is HttpRequestError.ConnectionError
            or HttpRequestError.NameResolutionError
            or HttpRequestError.ResponseEnded
            || http.InnerException is IOException,
        StewardRequestException request => IsTransient(request.StatusCode),
        TaskCanceledException { InnerException: TimeoutException } => true,
        TimeoutException or IOException => true,
        _ => false,
    };

    public static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var header = response.Headers.RetryAfter;
        var delay = header?.Delta ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        return delay is { } value ? TimeSpan.FromTicks(Math.Clamp(value.Ticks, 0, MaxBackoff.Ticks)) : null;
    }
}

public sealed class TransientRetryHandler(IReadOnlyList<TimeSpan>? delays = null) : DelegatingHandler
{
    private readonly IReadOnlyList<TimeSpan> _delays = delays ?? [.. Enumerable.Range(0, 4).Select(TransientHttp.Backoff)];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var retryable = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head
            || (request.Options.TryGetValue(TransientHttp.Idempotent, out var idempotent) && idempotent);
        for (var attempt = 0; ; attempt++)
        {
            var last = !retryable || attempt >= _delays.Count;
            TimeSpan delay;
            try
            {
                var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (last || !TransientHttp.IsTransient(response.StatusCode))
                {
                    return response;
                }

                delay = TransientHttp.RetryAfter(response) ?? _delays[attempt];
                response.Dispose();
            }
            catch (Exception ex) when (!last && !cancellationToken.IsCancellationRequested && TransientHttp.IsTransient(ex))
            {
                delay = _delays[attempt];
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }
}
