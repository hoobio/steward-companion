using System.Runtime.InteropServices;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Steward.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();

        Application.Start(_ =>
        {
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            var context = new DispatcherQueueSynchronizationContext(dispatcherQueue);
            SynchronizationContext.SetSynchronizationContext(context);

            var app = new App();
            GC.KeepAlive(app);
        });

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
