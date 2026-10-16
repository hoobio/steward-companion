using Steward.Core;
using Steward.Core.Diagnostics;

namespace Steward.App.Services;

public sealed partial class InstanceCoordination
{
    private const int MaxLinkLength = 2048;

    public string? Link { get; private init; }

    private static void Log(string ownTrain, Exception? exception, string message)
    {
        var logDirectory = Path.Combine(LocalData.Folder, "logs");
        using var provider = new FileLoggerProvider(logDirectory, ownTrain);
        provider.CreateLogger("Steward.App.Program").Warn(exception, message);
    }
}
