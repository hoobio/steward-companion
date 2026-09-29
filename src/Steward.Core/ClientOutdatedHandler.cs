using System.Net;
using System.Text.Json;

namespace Steward.Core;

public sealed class ClientOutdatedException(string message)
    : HttpRequestException(message, null, HttpStatusCode.Gone)
{
    public const string FallbackMessage = "This version of Steward is no longer supported. Update Steward to keep using it.";
}

public sealed class ClientOutdatedHandler(Action<string>? onOutdated = null) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Gone)
        {
            return response;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!TryReadOutdated(body, out var message))
        {
            return response;
        }

        response.Dispose();
        onOutdated?.Invoke(message);
        throw new ClientOutdatedException(message);
    }

    private static bool TryReadOutdated(string body, out string message)
    {
        message = ClientOutdatedException.FallbackMessage;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.String
                || error.GetString() != "client_outdated")
            {
                return false;
            }

            if (root.TryGetProperty("message", out var text) && text.ValueKind == JsonValueKind.String && text.GetString() is { Length: > 0 } server)
            {
                message = server;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
