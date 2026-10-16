using System.ComponentModel;
using System.Diagnostics;

namespace Steward.Core;

public static class ProcessCommand
{
    public static CommandResult? Run(string program, IReadOnlyList<string> arguments) =>
        RunAsync([program, .. arguments], CancellationToken.None).GetAwaiter().GetResult();

    public static async Task<CommandResult?> RunAsync(IReadOnlyList<string> argv, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(argv);
        using var process = new Process { StartInfo = StartInfo(argv, redirect: true) };
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            return null;
        }

        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new CommandResult(process.ExitCode, (await stdout.ConfigureAwait(false)) + (await stderr.ConfigureAwait(false)));
    }

    public static bool StartDetached(IReadOnlyList<string> argv)
    {
        ArgumentNullException.ThrowIfNull(argv);
        try
        {
            using var process = Process.Start(StartInfo(argv, redirect: false));
            return process is not null;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static ProcessStartInfo StartInfo(IReadOnlyList<string> argv, bool redirect)
    {
        var info = new ProcessStartInfo(argv[0])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect,
            RedirectStandardInput = redirect,
        };
        foreach (var argument in argv.Skip(1))
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }
}
