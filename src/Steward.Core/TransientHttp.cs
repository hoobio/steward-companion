using System.Net;

namespace Steward.Core;

public static class TransientHttp
{
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
}
