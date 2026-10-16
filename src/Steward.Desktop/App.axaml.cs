using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Steward.App;
using Steward.App.Hosting;
using Steward.App.Services;
using Steward.App.ViewModels;
using Steward.Core;
using Steward.Core.Diagnostics;
using Steward.Desktop.Services;
using Steward.Desktop.Styles;
using Steward.Desktop.Views;

namespace Steward.Desktop;

public sealed partial class App : Application
{
    private readonly NativeMenuItem _about = new("About Steward");
    private IHost? _host;

    public static InstanceCoordination? Coordination { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        if (OperatingSystem.IsMacOS())
        {
            // Avalonia.Native adds Hide and Quit only to the app menu it first exports, which happens before OnFrameworkInitializationCompleted.
            NativeMenu.SetMenu(this, [_about]);
        }

        PopupPlacement.Register();
        DialogResourceScope.Register();
        ScrollIndicator.Register();
        AppFonts.UseGrayscaleText();
    }

    public static IHost BuildHost(AvaloniaPlatform platform, string dataFolder, Func<HttpMessageHandler>? primaryHandler = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddJsonStream(typeof(App).Assembly.GetManifestResourceStream("Steward.Desktop.appsettings.json")
            ?? throw new InvalidOperationException("appsettings.json is not embedded"));
        builder.AddStewardServices(platform, dataFolder);
        builder.Services.AddSingleton(platform);
        if (primaryHandler is not null)
        {
            builder.Services.AddHttpClient("Steward").ConfigurePrimaryHttpMessageHandler(primaryHandler);
            builder.Services.AddHttpClient("Addon").ConfigurePrimaryHttpMessageHandler(primaryHandler);
        }

        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<HomePage>();
        builder.Services.AddSingleton<SyncPage>();
        builder.Services.AddSingleton<GuidesPage>();
        builder.Services.AddSingleton<SettingsPage>();
        var host = builder.Build();
        platform.Http = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("Addon");
        return host;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || Coordination is not { } coordination)
        {
            return;
        }

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var platform = new AvaloniaPlatform();
        _host = BuildHost(platform, StewardServices.DefaultDataFolder);
        desktop.Exit += (_, _) => _host.Dispose();

        var logger = _host.Services.GetRequiredService<ILogger<App>>();
        logger.Info($"Steward {AppInfo.StoreDisplayVersion ?? MainViewModel.InstalledVersion} starting ({AppInfo.LogName}, {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture})");
        Dispatcher.UIThread.UnhandledException += (_, e) => logger.Crit(e.Exception, "Unhandled UI exception");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            logger.Crit(e.ExceptionObject as Exception, "Unhandled AppDomain exception");
        TaskScheduler.UnobservedTaskException += (_, e) =>
            logger.Crit(e.Exception, "Unobserved task exception");

        var window = _host.Services.GetRequiredService<MainWindow>();
        var viewModel = window.ViewModel;
        window.CreateTrayIcon();
        _about.Click += (_, _) =>
        {
            window.ShowFromTray();
            viewModel.NavigateToPageTag?.Invoke("settings");
        };

        coordination.ListenForSignals(
            platform,
            () =>
            {
                logger.Info("Show signal from a same-train launch");
                window.ShowFromTray();
            },
            window.QuitFromAnotherBuildAsync,
            viewModel.ReceiveCurseForgeLinkAsync);
        if (OperatingSystem.IsMacOS() && this.TryGetFeature<IActivatableLifetime>() is { } activatable)
        {
            activatable.Activated += (_, e) =>
            {
                if (e is ProtocolActivatedEventArgs { Uri: { } uri } && string.Equals(uri.Scheme, CurseForgeLinks.Scheme, StringComparison.OrdinalIgnoreCase))
                {
                    Dispatcher.UIThread.Post(() => _ = viewModel.ReceiveCurseForgeLinkAsync(uri.OriginalString));
                }
            };
        }

        if (coordination.ClosedTrainDisplayName is { } closedTrainDisplayName)
        {
            viewModel.ShowLocalInfoBanner($"Closed the {closedTrainDisplayName} build automatically");
        }

        var arguments = Environment.GetCommandLineArgs();
        if (GuidesPreview.Scenario(arguments) is { Length: > 0 } scenario)
        {
            GuidesPreview.Apply(viewModel, scenario);
            window.ShowFromTray();
            window.ShowGuidesPreview(scenario);
            return;
        }

        if (PopupsPreview.Scenario(arguments) is { Length: > 0 } popups)
        {
            BannersPreview.Apply(viewModel);
            window.ShowFromTray();
            window.ShowPopupsPreview(popups);
            return;
        }

        if (BannersPreview.IsRequested(arguments))
        {
            BannersPreview.Apply(viewModel);
            window.ShowFromTray();
            return;
        }

        if (UpdateProgressPreview.IsRequested(arguments))
        {
            UpdateProgressPreview.Apply(viewModel);
            window.ShowFromTray();
            return;
        }

        if (ClientOutdatedPreview.IsRequested(arguments))
        {
            ClientOutdatedPreview.Apply(viewModel);
            if (arguments.Contains(StartupRegistration.TrayArgument))
            {
                window.HideToTray();
            }
            else
            {
                window.ShowFromTray();
            }

            return;
        }

        if (LiveUpdatesPreview.IsRequested(arguments))
        {
            LiveUpdatesPreview.Apply(viewModel);
            window.ShowFromTray();
            return;
        }

        var tray = arguments.Contains(StartupRegistration.TrayArgument);
        var withoutFocus = tray || arguments.Contains(StartupRegistration.NoActivateArgument);
        if (withoutFocus)
        {
            window.ForegroundGate.Hold();
        }

        var initializing = viewModel.InitializeCommand.ExecuteAsync(null);
        if (tray && viewModel.MinimizeToTray)
        {
            window.HideToTray();
        }
        else if (withoutFocus)
        {
            window.ShowWithoutFocus();
        }
        else
        {
            window.ShowFromTray();
        }

        _ = initializing.ContinueWith(_ => Dispatcher.UIThread.Post(window.TrimIfHidden), TaskScheduler.Default);
        if (coordination.Link is { } link)
        {
            _ = viewModel.ReceiveCurseForgeLinkAsync(link);
        }
    }
}
