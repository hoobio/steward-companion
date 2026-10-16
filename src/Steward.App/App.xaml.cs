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
    private readonly InstanceCoordination _instanceCoordination;
    private IHost? _host;
    private MainWindow? _window;

    public App(InstanceCoordination instanceCoordination)
    {
        _instanceCoordination = instanceCoordination;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
        builder.ConfigureSteward(new WinUiPlatform(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
        _host = builder.Build();

        var logger = _host.Services.GetRequiredService<ILogger<App>>();
        UnhandledException += (_, e) => logger.Crit(e.Exception, "Unhandled UI exception");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            logger.Crit(e.ExceptionObject as Exception, "Unhandled AppDomain exception");
        TaskScheduler.UnobservedTaskException += (_, e) =>
            logger.Crit(e.Exception, "Unobserved task exception");

        _window = _host.Services.GetRequiredService<MainWindow>();
        _window.Closed += (_, _) => _host.Dispose();
        _instanceCoordination.ListenForSignals(
            _window.ViewModel.Platform,
            _window.ShowFromTray,
            _window.QuitFromAnotherBuildAsync,
            _window.ViewModel.ReceiveCurseForgeLinkAsync);
        if (_instanceCoordination.ClosedTrainDisplayName is { } closedTrainDisplayName)
        {
            _window.ViewModel.ShowLocalInfoBanner($"Closed the {closedTrainDisplayName} build automatically");
        }

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

        if (UpdateProgressPreview.IsRequested(Environment.GetCommandLineArgs()))
        {
            UpdateProgressPreview.Apply(_window.ViewModel);
            _window.Activate();
            return;
        }

        if (ClientOutdatedPreview.IsRequested(Environment.GetCommandLineArgs()))
        {
            ClientOutdatedPreview.Apply(_window.ViewModel);
            if (Environment.GetCommandLineArgs().Contains(StartupRegistration.TrayArgument))
            {
                _window.HideToTray();
            }
            else
            {
                _window.Activate();
            }

            return;
        }

        if (LiveUpdatesPreview.IsRequested(Environment.GetCommandLineArgs()))
        {
            LiveUpdatesPreview.Apply(_window.ViewModel);
            _window.Activate();
            return;
        }

        var tray = Environment.GetCommandLineArgs().Contains(StartupRegistration.TrayArgument);
        if (tray && _window.ViewModel.MinimizeToTray)
        {
            _window.HideToTray();
        }
        else if (tray || Environment.GetCommandLineArgs().Contains(StartupRegistration.NoActivateArgument))
        {
            _window.ShowWithoutFocus();
        }
        else
        {
            _window.ShowFromTray();
        }
        _ = _window.ViewModel.InitializeCommand.ExecuteAsync(null)
            .ContinueWith(_ => _window.DispatcherQueue.TryEnqueue(_window.TrimIfHidden), TaskScheduler.Default);
        if (_instanceCoordination.Link is { } link)
        {
            _ = _window.ViewModel.ReceiveCurseForgeLinkAsync(link);
        }
    }
}
