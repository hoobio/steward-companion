using Microsoft.Extensions.Logging;

namespace Steward.Core.Diagnostics;

// One reusable set of LoggerMessage delegates (CA1848) rather than one per call site; this app's own file sink writes plain text, so the pre-formatted message loses nothing a structured template would have kept.
public static partial class DiagnosticLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "{Message}")]
    public static partial void Info(this ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Message}")]
    public static partial void Warn(this ILogger logger, Exception? exception, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Message}")]
    public static partial void Err(this ILogger logger, Exception? exception, string message);

    [LoggerMessage(Level = LogLevel.Critical, Message = "{Message}")]
    public static partial void Crit(this ILogger logger, Exception? exception, string message);
}
