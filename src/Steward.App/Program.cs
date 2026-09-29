using System.Runtime.InteropServices;
using System.Threading;

using Steward.App.Services;
using Steward.Core;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

using Windows.ApplicationModel.Activation;

namespace Steward.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();

        if (!InstanceCoordination.TryAcquire(ActivationLink(args), out var coordination))
        {
            return 0;
        }

        Application.Start(_ =>
        {
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            var context = new DispatcherQueueSynchronizationContext(dispatcherQueue);
            SynchronizationContext.SetSynchronizationContext(context);

            var app = new App(coordination!);
            GC.KeepAlive(app);
        });

        coordination!.Dispose();
        return 0;
    }

    private const string LinkSwitch = "--curseforge-link=";

    private static string? ActivationLink(string[] args) =>
        args.Select(arg => arg.StartsWith(LinkSwitch, StringComparison.Ordinal) ? arg[LinkSwitch.Length..] : arg)
            .FirstOrDefault(arg => arg.StartsWith($"{CurseForgeLinks.Scheme}:", StringComparison.OrdinalIgnoreCase))
        ?? (App.IsPackaged
            && AppInstance.GetCurrent().GetActivatedEventArgs() is { Kind: ExtendedActivationKind.Protocol, Data: IProtocolActivatedEventArgs protocol }
                ? protocol.Uri.AbsoluteUri
                : null);
}

internal static class ComWrappersSupport
{
    [DllImport("Microsoft.UI.Xaml.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern void XamlCheckProcessRequirements();

    public static void InitializeComWrappers()
    {
        XamlCheckProcessRequirements();
        WinRT.ComWrappersSupport.InitializeComWrappers();
    }
}
