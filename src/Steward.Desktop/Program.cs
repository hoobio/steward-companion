using Avalonia;

using Steward.App;
using Steward.App.Services;
using Steward.Core;
using Steward.Desktop.Services;
using Steward.Desktop.Styles;

namespace Steward.Desktop;

public static partial class Program
{
    private const string LinkSwitch = "--curseforge-link=";

    [STAThread]
    public static int Main(string[] args)
    {
        var withoutFocus = args.Contains(StartupRegistration.TrayArgument) || args.Contains(StartupRegistration.NoActivateArgument);
        UiWatchdog.WaitForPreviousInstance(args);
        if (!InstanceCoordination.TryAcquire(ActivationLink(args), out var coordination, showRunning: !withoutFocus))
        {
            return 0;
        }

        App.Coordination = coordination;
        ConfigurePlatform();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            coordination!.Dispose();
        }

        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseStewardFonts()
        .With(new Win32PlatformOptions
        {
            OverlayPopups = AppInfo.IsWine,
            RenderingMode = AppInfo.IsWine ? [Win32RenderingMode.Software] : [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software],
            CompositionMode = AppInfo.IsWine
                ? [Win32CompositionMode.RedirectionSurface]
                : [Win32CompositionMode.WinUIComposition, Win32CompositionMode.DirectComposition, Win32CompositionMode.RedirectionSurface],
        })
#pragma warning disable AVALONIA_X11_CSD
        .With(new X11PlatformOptions { EnableDrawnDecorations = true, OverlayPopups = !X11Compositor.IsRunning(), WmClass = "Steward" });
#pragma warning restore AVALONIA_X11_CSD

    static partial void ConfigurePlatform();

    private static string? ActivationLink(string[] args) =>
        args.Select(arg => arg.StartsWith(LinkSwitch, StringComparison.Ordinal) ? arg[LinkSwitch.Length..] : arg)
            .FirstOrDefault(arg => arg.StartsWith($"{CurseForgeLinks.Scheme}:", StringComparison.OrdinalIgnoreCase) || AddonSettingsPayload.IsImportLink(arg))
        ?? WindowsShell.ProtocolActivationUri();
}
