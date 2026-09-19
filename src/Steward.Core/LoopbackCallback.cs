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
}
