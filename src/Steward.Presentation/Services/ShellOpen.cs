using System.ComponentModel;
using System.Diagnostics;

namespace Steward.App.Services;

public static class ShellOpen
{
    private const int XdgOpenNoMethod = 3;

    public static event Action<string, string>? Failed;

    internal static Action<string>? Launch { get; set; }

    public static void Open(string target)
    {
        if (Launch is { } launch)
        {
            launch(target);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            return;
        }

        _ = OpenUnixAsync(target);
    }

    // UseShellExecute returns as soon as xdg-open starts, and xdg-open reports a missing handler only through its exit code.
    private static async Task OpenUnixAsync(string target)
    {
        var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        string? failure;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(opener, [target]) { UseShellExecute = false });
            if (process is null)
            {
                failure = $"{opener} did not start";
            }
            else
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
                failure = process.ExitCode switch
                {
                    0 => null,
                    XdgOpenNoMethod when !OperatingSystem.IsMacOS() => "no application is set up to open it",
                    var code => $"{opener} exited with code {code}",
                };
            }
        }
        catch (Win32Exception ex)
        {
            failure = $"{opener} could not be started ({ex.Message})";
        }

        if (failure is not null)
        {
            Failed?.Invoke(target, failure);
        }
    }
}
