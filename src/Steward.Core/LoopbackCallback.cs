using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Steward.Core;

public static partial class LoopbackCallback
{
    [GeneratedRegex(@"^GET /\?code=([A-Za-z0-9_-]+) HTTP/", RegexOptions.CultureInvariant)]
    private static partial Regex CallbackPattern { get; }

    public static bool TryReadCode(string? requestLine, out string code)
    {
        var match = requestLine is null ? null : CallbackPattern.Match(requestLine);
        code = match is { Success: true } ? match.Groups[1].Value : "";
        return match is { Success: true };
    }

    public static async Task<string> WaitForTokenAsync(
        TcpListener listener,
        string responseBody,
        Func<string, CancellationToken, Task<string>> exchange,
        CancellationToken cancellationToken)
    {
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
            try
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                continue;
            }

            if (!TryReadCode(requestLine, out var code))
            {
                continue;
            }

            try
            {
                return await exchange(code, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
