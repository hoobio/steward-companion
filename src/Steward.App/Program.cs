using System.Runtime.InteropServices;
using System.Threading;

using Steward.App.Services;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Steward.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!InstanceCoordination.TryAcquire(out var coordination))
        {
            return 0;
        }

        ComWrappersSupport.InitializeComWrappers();

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
