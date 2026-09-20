using System.Reflection;

using Steward.App.Hosting;
using Steward.App.Views;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;

namespace Steward.App;

public partial class App : Application
{
    public static readonly string IconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Steward.ico");

    public static readonly bool IsGitHubRelease = typeof(App).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(attribute => attribute.Key == "GitHubRelease" && attribute.Value == "true");

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

        _window = _host.Services.GetRequiredService<MainWindow>();
        _window.Closed += (_, _) => _host.Dispose();
        if (Environment.GetCommandLineArgs().Contains(StartupRegistration.TrayArgument) && _window.ViewModel.KeepInTray)
        {
            _window.HideToTray();
        }
        else
        {
            _window.Activate();
        }
        _ = _window.ViewModel.InitializeCommand.ExecuteAsync(null);
    }
}
