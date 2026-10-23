using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

using Microsoft.Extensions.Logging;

using Steward.App;
using Steward.Core.Diagnostics;

namespace Steward.Desktop.Services;

public sealed class UiWatchdog(Window window, ILogger logger)
{
    public const string RestartAfterArgument = "--restart-after=";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RelaunchGuard = TimeSpan.FromMinutes(5);
    private const int MissedBeatsBeforeRestart = 6;

    private volatile int _stage;
    private volatile bool _visible = true;

    public static int? RestartedFrom(string[] args) =>
        args.FirstOrDefault(arg => arg.StartsWith(RestartAfterArgument, StringComparison.Ordinal)) is { } arg
        && int.TryParse(arg[RestartAfterArgument.Length..], out var pid) ? pid : null;

    public static void WaitForPreviousInstance(string[] args)
    {
        if (RestartedFrom(args) is not { } pid)
        {
            return;
        }

        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(TimeSpan.FromSeconds(15));
        }
        catch (ArgumentException)
        {
        }
    }

    public void Start()
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        new Thread(() => Run(handle)) { IsBackground = true, Name = "UI watchdog" }.Start();
    }

    private void Run(nint handle)
    {
        var missed = 0;
        while (true)
        {
            _stage = 0;
            // Dispatcher.Post takes the dispatcher's lock, so a UI thread stuck while holding it must not also stall this loop.
            ThreadPool.UnsafeQueueUserWorkItem(_ => Dispatcher.UIThread.Post(Beat), null);
            Thread.Sleep(Interval);
            while (_stage < 2 || Native.IsHungAppWindow(handle))
            {
                if (++missed >= MissedBeatsBeforeRestart && !Debugger.IsAttached)
                {
                    Restart(_stage switch { 0 => "UI thread", 1 => "render thread", _ => "window message loop" });
                    return;
                }

                Thread.Sleep(Interval);
            }

            missed = 0;
        }
    }

    private void Beat()
    {
        _stage = 1;
        _visible = window.IsVisible;
        if (ElementComposition.GetElementVisual(window)?.Compositor is { } compositor)
        {
            _ = ProbeRenderThreadAsync(compositor);
        }
        else
        {
            _stage = 2;
        }
    }

    // The GPU interop job runs on the render thread only once its graphics context is valid, so its completion shows both threads are alive.
    private async Task ProbeRenderThreadAsync(Compositor compositor)
    {
        await compositor.TryGetCompositionGpuInterop();
        _stage = 2;
    }

    private void Restart(string stalled)
    {
        var restarted = RestartedFrom(Environment.GetCommandLineArgs()) is not null
            && DateTime.Now - Process.GetCurrentProcess().StartTime < RelaunchGuard;
        if (restarted)
        {
            logger.Warn(null, $"The {stalled} stopped responding again within {RelaunchGuard.TotalMinutes:0} minutes of a restart; not restarting again");
            return;
        }

        var mode = _visible ? StartupRegistration.NoActivateArgument : StartupRegistration.TrayArgument;
        logger.Warn(null, $"The {stalled} stopped responding for {(Interval * MissedBeatsBeforeRestart).TotalSeconds:0}s, most likely after a GPU driver reset; restarting with {mode}");
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { ArgumentList = { mode, $"{RestartAfterArgument}{Environment.ProcessId}" } });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.Warn(e, "Could not start a new Steward process; not restarting");
            return;
        }

        Process.GetCurrentProcess().Kill();
    }
}
