using System.Net;

using Steward.App.Services;
using Steward.App.ViewModels;
using Steward.App.Views;
using Steward.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Steward.App.Hosting;

internal static class HostBuilderExtensions
{
    public static HostApplicationBuilder ConfigureSteward(this HostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.AddDebug();

        var baseUrl = builder.Configuration["Gigagrug:BaseUrl"]
            ?? throw new InvalidOperationException("Gigagrug:BaseUrl is not configured");
        var addons = builder.Configuration.GetSection("Addons").Get<ManagedAddon[]>()
            ?? throw new InvalidOperationException("Addons is not configured");
        if (addons.Length == 0)
        {
            throw new InvalidOperationException("Addons must list at least one managed addon");
        }

        builder.Services.AddSingleton<CookieContainer>();
        builder.Services.AddHttpClient("Gigagrug")
            .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
            {
                CookieContainer = sp.GetRequiredService<CookieContainer>(),
            });
        builder.Services.AddHttpClient("Addon");

        builder.Services.AddSingleton<IReadOnlyList<ManagedAddon>>(addons);
        builder.Services.AddSingleton(sp => new AppStateStore([.. sp.GetRequiredService<IReadOnlyList<ManagedAddon>>().Select(a => a.Id)]));
        builder.Services.AddSingleton<ISessionService>(sp => new SessionService(
            sp.GetRequiredService<CookieContainer>(),
            sp.GetRequiredService<AppStateStore>(),
            baseUrl));
        builder.Services.AddSingleton(sp => new GigagrugClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("Gigagrug"),
            baseUrl));
        builder.Services.AddSingleton(sp => new AddonUpdater(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("Addon")));

        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<MainViewModel>();

        return builder;
    }
}
