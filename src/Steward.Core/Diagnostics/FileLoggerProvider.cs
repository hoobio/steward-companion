using System.Globalization;

using Microsoft.Extensions.Logging;

namespace Steward.Core.Diagnostics;

public sealed class FileLoggerProvider : ILoggerProvider
{
    public const int RetentionDays = 14;

    private readonly string _directory;
    private readonly string _buildName;
    private readonly Lock _writeLock = new();
    private readonly Func<DateTimeOffset> _now;
    private StreamWriter? _writer;
    private DateOnly _writerDate;

    public FileLoggerProvider(string directory, string buildName) : this(directory, buildName, () => DateTimeOffset.UtcNow)
    {
    }

    internal FileLoggerProvider(string directory, string buildName, Func<DateTimeOffset> now)
    {
        _directory = directory;
        _buildName = buildName;
        _now = now;
        Directory.CreateDirectory(_directory);
        LogFileRetention.DeleteOlderThan(_directory, RetentionDays, now());
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void Write(string categoryName, LogLevel level, string message, Exception? exception)
    {
        var now = _now();
        lock (_writeLock)
        {
            var writer = WriterFor(now);
            writer.Write(now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
            writer.Write(' ');
            writer.Write(LevelLabel(level));
            writer.Write(' ');
            writer.Write(categoryName);
            writer.Write(": ");
            writer.WriteLine(message);
            if (exception is not null)
            {
                writer.WriteLine(exception.ToString());
            }

            writer.Flush();
        }
    }

    private StreamWriter WriterFor(DateTimeOffset now)
    {
        var date = DateOnly.FromDateTime(now.UtcDateTime);
        if (_writer is not null && date == _writerDate)
        {
            return _writer;
        }

        _writer?.Dispose();
        _writerDate = date;
        var path = Path.Combine(_directory, LogFileRetention.FileNameFor(_buildName, date));
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream) { AutoFlush = false };
        return _writer;
    }

    private static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    public void Dispose()
    {
        lock (_writeLock)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Write(categoryName, logLevel, formatter(state, exception), exception);
        }
    }
}
