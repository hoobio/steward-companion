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
        var appManifestBaseUrl = builder.Configuration["App:ManifestBaseUrl"]
            ?? throw new InvalidOperationException("App:ManifestBaseUrl is not configured");
        var storeProductId = builder.Configuration["App:StoreProductId"]
            ?? throw new InvalidOperationException("App:StoreProductId is not configured");
        var addons = builder.Configuration.GetSection("Addons").Get<ManagedAddon[]>()
            ?? throw new InvalidOperationException("Addons is not configured");
        if (addons.Length == 0)
        {
            throw new InvalidOperationException("Addons must list at least one managed addon");
        }

        var restedXpAccountUrl = builder.Configuration["RestedXp:AccountBaseUrl"]
            ?? throw new InvalidOperationException("RestedXp:AccountBaseUrl is not configured");
        var restedXpGuidesUrl = builder.Configuration["RestedXp:GuidesBaseUrl"]
            ?? throw new InvalidOperationException("RestedXp:GuidesBaseUrl is not configured");
        var keycloakTokenUrl = builder.Configuration["RestedXp:Keycloak:TokenUrl"]
            ?? throw new InvalidOperationException("RestedXp:Keycloak:TokenUrl is not configured");
        var keycloakClientId = builder.Configuration["RestedXp:Keycloak:ClientId"]
            ?? throw new InvalidOperationException("RestedXp:Keycloak:ClientId is not configured");
        var keycloakScope = builder.Configuration["RestedXp:Keycloak:Scope"]
            ?? throw new InvalidOperationException("RestedXp:Keycloak:Scope is not configured");

        var productPrefixes = builder.Configuration.GetSection("RestedXp:ProductPrefixes").Get<Dictionary<string, string[]>>()
            ?? throw new InvalidOperationException("RestedXp:ProductPrefixes is not configured");

        var supportedProducts = builder.Configuration.GetSection("SupportedProducts").Get<Dictionary<string, string>>();
        if (supportedProducts is null || supportedProducts.Count == 0)
        {
            throw new InvalidOperationException("SupportedProducts is not configured");
        }

        builder.Services.AddSingleton<CookieContainer>();
        builder.Services.AddHttpClient("Gigagrug")
            .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
            {
                CookieContainer = sp.GetRequiredService<CookieContainer>(),
            });
        builder.Services.AddHttpClient("Addon")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false });

        builder.Services.AddSingleton<IReadOnlyList<ManagedAddon>>(addons);
        builder.Services.AddSingleton<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>(supportedProducts, StringComparer.OrdinalIgnoreCase));
        builder.Services.AddSingleton(sp => new AppStateStore([.. sp.GetRequiredService<IReadOnlyList<ManagedAddon>>().Select(a => a.Id)]));
        builder.Services.AddSingleton(sp => new GigagrugClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("Gigagrug"),
            baseUrl));
        builder.Services.AddSingleton<ISessionService>(sp => new SessionService(
            sp.GetRequiredService<CookieContainer>(),
            sp.GetRequiredService<AppStateStore>(),
            sp.GetRequiredService<GigagrugClient>(),
            baseUrl));
        builder.Services.AddSingleton(sp => new AddonUpdater(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("Addon")));
        builder.Services.AddSingleton(sp => new AppUpdater(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Addon"), appManifestBaseUrl, storeProductId));

        builder.Services.AddSingleton(sp => new RestedXpClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("Addon"),
            restedXpAccountUrl,
            restedXpGuidesUrl,
            keycloakTokenUrl,
            keycloakClientId,
            keycloakScope));
        builder.Services.AddSingleton(sp => new RestedXpService(
            sp.GetRequiredService<RestedXpClient>(),
            sp.GetRequiredService<AppStateStore>(),
            new Dictionary<string, string[]>(productPrefixes, StringComparer.OrdinalIgnoreCase)));

        builder.Services.AddSingleton(sp => new DiscordImage(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("Addon")));

        builder.Services.AddSingleton<InMemoryGuildSyncApi>();
        builder.Services.AddSingleton<IGuildSyncApi>(sp => sp.GetRequiredService<InMemoryGuildSyncApi>());
        builder.Services.AddSingleton<GigagrugGuildSyncApi>();

        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<MainViewModel>();

        return builder;
    }
}
