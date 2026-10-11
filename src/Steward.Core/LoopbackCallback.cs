using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Steward.Core;

public sealed class SignInRefusedException(string message) : Exception(message)
{
    public const string NotMemberMessage = "You are not a member of a Steward server";
}

public static partial class LoopbackCallback
{
    public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);

    [GeneratedRegex(@"^GET /\?code=([A-Za-z0-9_-]+) HTTP/", RegexOptions.CultureInvariant)]
    private static partial Regex CallbackPattern { get; }

    [GeneratedRegex(@"^GET /\?(?:[^ ]*&)?error=([A-Za-z0-9_-]+)(?:&[^ ]*)? HTTP/", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorPattern { get; }

    public static bool TryReadCode(string? requestLine, out string code)
    {
        var match = requestLine is null ? null : CallbackPattern.Match(requestLine);
        code = match is { Success: true } ? match.Groups[1].Value : "";
        return match is { Success: true };
    }

    public static bool TryReadError(string? requestLine, out string error)
    {
        var match = requestLine is null ? null : ErrorPattern.Match(requestLine);
        error = match is { Success: true } ? match.Groups[1].Value : "";
        return match is { Success: true };
    }

    public static async Task<string> WaitForTokenAsync(
        TcpListener listener,
        string responseBody,
        Func<string, CancellationToken, Task<string>> exchange,
        CancellationToken cancellationToken,
        TimeSpan? connectionTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(exchange);
        var response = Encoding.UTF8.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {Encoding.UTF8.GetByteCount(responseBody)}\r\n" +
            "Connection: close\r\n" +
            "\r\n" +
            responseBody);

        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            string? requestLine;
            using (var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connection.CancelAfter(connectionTimeout ?? ConnectionTimeout);
                try
                {
                    var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    requestLine = await reader.ReadLineAsync(connection.Token).ConfigureAwait(false);
                    await stream.WriteAsync(response, connection.Token).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    continue;
                }
            }

            if (TryReadError(requestLine, out var error) && error == "not_member")
            {
                throw new SignInRefusedException(SignInRefusedException.NotMemberMessage);
            }

            if (!TryReadCode(requestLine, out var code))
            {
                continue;
            }

            try
            {
                return await exchange(code, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException
                || (ex is HttpRequestException && ex is not ClientOutdatedException)
                || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
            }
        }
    }
}
