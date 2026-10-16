using System.Runtime.InteropServices;
using System.Text;

namespace Steward.Desktop.Services;

internal static class X11Compositor
{
    private const string LibX11 = "libX11.so.6";

    public static bool IsRunning()
    {
        if (!OperatingSystem.IsLinux())
        {
            return true;
        }

        try
        {
            var display = XOpenDisplay(nint.Zero);
            if (display == nint.Zero)
            {
                return true;
            }

            try
            {
                // A compositing manager owns the _NET_WM_CM_S<screen> selection (EWMH).
                var selection = XInternAtom(display, Encoding.ASCII.GetBytes($"_NET_WM_CM_S{XDefaultScreen(display)}\0"), onlyIfExists: false);
                return XGetSelectionOwner(display, selection) != nint.Zero;
            }
            finally
            {
                _ = XCloseDisplay(display);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return true;
        }
    }

    [DllImport(LibX11)]
    private static extern nint XOpenDisplay(nint name);

    [DllImport(LibX11)]
    private static extern int XDefaultScreen(nint display);

    [DllImport(LibX11)]
    private static extern nint XInternAtom(nint display, byte[] name, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [DllImport(LibX11)]
    private static extern nint XGetSelectionOwner(nint display, nint selection);

    [DllImport(LibX11)]
    private static extern int XCloseDisplay(nint display);
}
