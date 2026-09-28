using System.Reflection;
using System.Runtime.InteropServices;

using Steward.App.Hosting;
using Steward.App.Services;
using Steward.App.Views;
using Steward.Core.Diagnostics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace Steward.App;

public partial class App : Application
{
    public static readonly string IconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Steward.ico");

    public static readonly bool IsGitHubRelease = typeof(App).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(attribute => attribute.Key == "GitHubRelease" && attribute.Value == "true");

    public static readonly bool IsPreRelease = typeof(App).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(attribute => attribute.Key == "StorePreRelease" && attribute.Value == "true");

    public const string PackageFamilyName = "Hoobi.Steward_thayxpy3eqg0g";

    public const string MsiUpgradeCode = "{CCD0BF88-7A8E-4F74-9DB7-9B9272B3D503}";

    private const int AppModelErrorNoPackage = 15700;

    public static readonly bool IsPackaged = ResolveIsPackaged();

    public static readonly string BuildName = ResolveBuildName();

    private static readonly string RealDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steward");

    public static string DisplayDataFolder(string realDataFolder)
    {
        if (!IsPackaged)
        {
            return realDataFolder;
        }

        var relative = Path.GetRelativePath(RealDataRoot, realDataFolder);
        var virtualised = Path.Combine(Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path, "Local", "Steward", relative);
        return Directory.Exists(virtualised) ? virtualised : realDataFolder;
    }

    private IHost? _host;
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: false);
        builder.ConfigureSteward();
        _host = builder.Build();

        var logger = _host.Services.GetRequiredService<ILogger<App>>();
        UnhandledException += (_, e) => logger.Crit(e.Exception, "Unhandled UI exception");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            logger.Crit(e.ExceptionObject as Exception, "Unhandled AppDomain exception");
        TaskScheduler.UnobservedTaskException += (_, e) =>
            logger.Crit(e.Exception, "Unobserved task exception");

        _window = _host.Services.GetRequiredService<MainWindow>();
        _window.Closed += (_, _) => _host.Dispose();

        if (GuidesPreview.Scenario(Environment.GetCommandLineArgs()) is { Length: > 0 } scenario)
        {
            GuidesPreview.Apply(_window.ViewModel, scenario);
            _window.Activate();
            _window.ShowGuidesPreview(scenario);
            return;
        }

        if (BannersPreview.IsRequested(Environment.GetCommandLineArgs()))
        {
            BannersPreview.Apply(_window.ViewModel);
            _window.Activate();
            return;
        }

        if (LiveUpdatesPreview.IsRequested(Environment.GetCommandLineArgs()))
        {
            LiveUpdatesPreview.Apply(_window.ViewModel);
            _window.Activate();
            return;
        }

        if (Environment.GetCommandLineArgs().Contains(StartupRegistration.TrayArgument) && _window.ViewModel.MinimizeToTray)
        {
            _window.HideToTray();
        }
        else
        {
            _window.ShowFromTray();
        }
        _ = _window.ViewModel.InitializeCommand.ExecuteAsync(null);
    }

    private static bool ResolveIsPackaged()
    {
        var length = 0;
        return GetCurrentPackageFullName(ref length, nint.Zero) != AppModelErrorNoPackage;
    }

    private static string ResolveBuildName() => (IsPackaged, IsGitHubRelease, IsPreRelease) switch
    {
        (true, true, true) => "prerelease",
        (true, true, false) => "store",
        (true, false, _) => "dev",
        (false, true, _) => "msi",
        (false, false, _) => "debug",
    };

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, nint packageFullName);
}
