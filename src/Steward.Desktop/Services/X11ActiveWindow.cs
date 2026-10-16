using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

using Avalonia.Controls;

namespace Steward.Desktop.Services;

public static class X11ActiveWindow
{
    private const string LibX11 = "libX11.so.6";
    private const nint XaWindow = 33;

    public static readonly MethodInfo? SetActiveSpeculatively =
        Type.GetType("Avalonia.X11.X11Window, Avalonia.X11")?.GetMethod("SetActiveSpeculatively", BindingFlags.NonPublic | BindingFlags.Instance);

    // Avalonia's X11 tracker only reacts to a change of _NET_ACTIVE_WINDOW, so a window whose reused XID equals the stale value never activates; this applies the state X already reports.
    public static bool ActivateIfActive(Window window)
    {
        if (window.IsActive
            || SetActiveSpeculatively?.DeclaringType?.IsInstanceOfType(window.PlatformImpl) != true
            || window.TryGetPlatformHandle() is not { HandleDescriptor: "XID" } handle
            || Get() != handle.Handle)
        {
            return false;
        }

        SetActiveSpeculatively!.Invoke(window.PlatformImpl, null);
        return window.IsActive;
    }

    public static nint? Get()
    {
        try
        {
            var display = XOpenDisplay(nint.Zero);
            if (display == nint.Zero)
            {
                return null;
            }

            try
            {
                var atom = XInternAtom(display, Encoding.ASCII.GetBytes("_NET_ACTIVE_WINDOW\0"), onlyIfExists: true);
                if (atom == nint.Zero
                    || XGetWindowProperty(display, XDefaultRootWindow(display), atom, 0, 1, delete: false, XaWindow, out _, out var format, out var items, out _, out var data) != 0
                    || data == nint.Zero)
                {
                    return null;
                }

                try
                {
                    return format == 32 && items == 1 ? Marshal.ReadIntPtr(data) : null;
                }
                finally
                {
                    _ = XFree(data);
                }
            }
            finally
            {
                _ = XCloseDisplay(display);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport(LibX11)]
    private static extern nint XOpenDisplay(nint name);

    [DllImport(LibX11)]
    private static extern nint XDefaultRootWindow(nint display);

    [DllImport(LibX11)]
    private static extern nint XInternAtom(nint display, byte[] name, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [DllImport(LibX11)]
    private static extern int XGetWindowProperty(
        nint display, nint window, nint property, nint offset, nint length, [MarshalAs(UnmanagedType.Bool)] bool delete, nint type,
        out nint actualType, out int actualFormat, out nint items, out nint bytesAfter, out nint data);

    [DllImport(LibX11)]
    private static extern int XFree(nint data);

    [DllImport(LibX11)]
    private static extern int XCloseDisplay(nint display);
}
