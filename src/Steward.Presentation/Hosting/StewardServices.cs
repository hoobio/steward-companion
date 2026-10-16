using System.Net;

using Steward.App.Services;
using Steward.App.ViewModels;
using Steward.Core;
using Steward.Core.Diagnostics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Steward.App.Hosting;

public static class StewardServices
{
    public static readonly string DefaultDataFolder = LocalData.Folder;

    public static HostApplicationBuilder AddStewardServices(this HostApplicationBuilder builder, IUiPlatform platform, string dataFolder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton(platform);
        builder.Services.AddSingleton(new AppDataFolder(dataFolder));
        ManifestIcon.CacheFolder = Path.Combine(dataFolder, "icons");
        SecretProtection.Folder = Path.Combine(dataFolder, "secrets");

        builder.Logging.AddDebug();
        builder.Services.AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(Path.Combine(dataFolder, "logs"), AppInfo.LogName));
        builder.Logging.AddFilter("Steward", LogLevel.Information);
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("System", LogLevel.Warning);

        var baseUrl = builder.Configuration["Steward:BaseUrl"]
            ?? throw new InvalidOperationException("Steward:BaseUrl is not configured");
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

        var userAgent = $"Steward/{AppInfo.StoreDisplayVersion ?? MainViewModel.InstalledVersion} ({(AppInfo.IsPreRelease ? "prerelease" : MainViewModel.Channel)})";

        builder.Services.AddSingleton<CookieContainer>();
        builder.Services.AddHttpClient("Steward")
            // Steward API's _same_site check accepts Sec-Fetch-Site: none, a value browsers never let a page set.
            .ConfigureHttpClient(c => c.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none"))
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
            {
                CookieContainer = sp.GetRequiredService<CookieContainer>(),
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            })
            .AddHttpMessageHandler(sp => new ClientOutdatedHandler(message => sp.GetRequiredService<MainViewModel>().ReportClientOutdated(message)))
            .AddHttpMessageHandler(sp => new SessionRevokedHandler(() => sp.GetRequiredService<MainViewModel>().ReportSessionRevoked()));
        var downloadUserAgent = builder.Configuration["Downloads:UserAgent"];
        builder.Services.AddHttpClient("Addon")
            .ConfigureHttpClient(c =>
            {
                if (!string.IsNullOrWhiteSpace(downloadUserAgent))
                {
                    c.DefaultRequestHeaders.UserAgent.ParseAdd(downloadUserAgent);
                }
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false, PooledConnectionLifetime = TimeSpan.FromMinutes(15) });

        builder.Services.AddSingleton<IReadOnlyList<ManagedAddon>>(addons);
        builder.Services.AddSingleton<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>(supportedProducts, StringComparer.OrdinalIgnoreCase));
        builder.Services.AddSingleton<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>(
            builder.Configuration.GetSection("CurseForge:GameVersionTypes").Get<Dictionary<string, int>>() ?? [],
            StringComparer.OrdinalIgnoreCase));
        builder.Services.AddSingleton(sp => new AppStateStore(
            [.. sp.GetRequiredService<IReadOnlyList<ManagedAddon>>().Select(a => a.Id)],
            Path.Combine(dataFolder, "state.json")));
        builder.Services.AddSingleton(sp => new StewardClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("Steward"),
            baseUrl,
            userAgent));
        builder.Services.AddSingleton<ISessionService>(sp => new SessionService(
            sp.GetRequiredService<CookieContainer>(),
            sp.GetRequiredService<AppStateStore>(),
            sp.GetRequiredService<StewardClient>(),
            baseUrl,
            sp.GetRequiredService<ILogger<SessionService>>()));
        builder.Services.AddSingleton(sp => new AddonUpdater(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("Addon"),
            sp.GetRequiredService<ILogger<AddonUpdater>>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("Steward"),
            sp.GetRequiredService<StewardClient>().ReportCurseForgeDownloadFailureAsync));
        builder.Services.AddSingleton(sp => new AppUpdater(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("Addon"),
            new Uri($"{baseUrl.TrimEnd('/')}/api/addons/steward-companion/latest-release.json"),
            storeProductId));

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
            new Dictionary<string, string[]>(productPrefixes, StringComparer.OrdinalIgnoreCase),
            Path.Combine(dataFolder, "guides"),
            sp.GetRequiredService<ILogger<RestedXpService>>()));

        builder.Services.AddSingleton(sp => new DiscordImage(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("Addon"),
            sp.GetRequiredService<IUiPlatform>()));

        builder.Services.AddSingleton<StewardGuildSyncApi>();
        builder.Services.AddSingleton<MainViewModel>();

        return builder;
    }
}
