using Steward.App.Services;
using Steward.App.Views;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Steward.App.Hosting;

internal static class HostBuilderExtensions
{
    public static HostApplicationBuilder ConfigureSteward(this HostApplicationBuilder builder, WinUiPlatform platform)
    {
        builder.AddStewardServices(platform, StewardServices.DefaultDataFolder);
        builder.Services.AddSingleton(platform);

        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<HomePage>();
        builder.Services.AddSingleton<SyncPage>();
        builder.Services.AddSingleton<GuidesPage>();
        builder.Services.AddSingleton<SettingsPage>();

        return builder;
    }
}
