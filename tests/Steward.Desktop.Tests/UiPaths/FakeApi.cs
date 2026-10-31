using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Steward.Desktop.Tests.UiPaths;

public sealed class FakeApi
{
    public const string AddonHost = "https://addon.test/";

    public ConcurrentQueue<string> Requests { get; } = new();

    public ConcurrentQueue<string> Unrouted { get; } = new();

    public bool Down { get; set; }

    public static string Me(params string[] features) =>
        $$"""
        {"user":{"id":"100","name":"Hoobi","username":"hoobi","avatar_url":null,"role":"global","features":[{{string.Join(",", features.Select(feature => $"\"{feature}\""))}}]},
         "guilds":[{"id":"1","name":"Forever Guild","icon_url":null,"member_count":12,"nick":null,"features":[{{string.Join(",", features.Select(feature => $"\"{feature}\""))}}]}],
         "addons":[
          {"id":"steward","name":"Steward","folder_name":"Steward","manifest_base_url":"{{AddonHost}}steward/","source":"Steward","auto_install":true},
          {"id":"steward-guides","name":"Steward: Guides","folder_name":"StewardGuides","manifest_base_url":"{{AddonHost}}steward-guides/","source":"Steward","parent":"steward"},
          {"id":"steward-auctioning","name":"Steward: Auctioning","folder_name":"StewardAuctioning","manifest_base_url":"{{AddonHost}}steward-auctioning/","source":"Steward","parent":"steward"},
          {"id":"steward-journey","name":"Steward: Journey","folder_name":"StewardJourney","manifest_base_url":"{{AddonHost}}steward-journey/","source":"Steward","parent":"steward"},
          {"id":"hoobiscripts","name":"HoobiScripts","folder_name":"HoobiScripts","manifest_base_url":"{{AddonHost}}hoobiscripts/","source":"Protected","notice":{"icon":"lock","text":"Protected addon, issued to your Steward account. Do not share it."} },
          {"id":"restedxp","name":"RestedXP Guides","folder_name":"RXPGuides","manifest_base_url":"{{AddonHost}}restedxp/","source":"GitHub"},
          {"id":"bugsack","name":"BugSack","folder_name":"BugSack","manifest_base_url":"https://api.hoobi.io/guild/api/addons/curseforge/6273/88568/","source":"CurseForge","curseforge":true},
          {"id":"buggrabber","name":"BugGrabber","folder_name":"!BugGrabber","manifest_base_url":"https://api.hoobi.io/guild/api/addons/curseforge/13344/88568/","source":"CurseForge","curseforge":true}
         ]}
        """;

    public static readonly IReadOnlyDictionary<string, (string? Release, string? PreRelease)> Versions = new Dictionary<string, (string?, string?)>
    {
        ["steward"] = (null, "0.0.0-d3f1d44"),
        ["steward-guides"] = (null, "0.0.0-5e1a2b3"),
        ["steward-auctioning"] = (null, "0.0.0-a7c9e01"),
        ["steward-journey"] = (null, "0.0.0-b2d4f68"),
        ["hoobiscripts"] = (null, "0.0.0-ec7e7a3"),
        ["restedxp"] = ("v4.11.22", "v4.11.23-beta"),
        ["6273"] = ("v11.2.0", "v11.2.1-beta"),
        ["13344"] = ("v11.0.3", null),
    };

    public const string TukUiList = """
        [
          {"id":-2,"slug":"elvui","author":"Elv","name":"ElvUI","url":"https://api.tukui.org/v1/download/elvui/token","version":"16.00","changelog_url":null,"patch":["12.1.0","5.5.4","1.15.9","2.5.6"],"last_update":"2026-10-13","web_url":"https://tukui.org/elvui","small_desc":"A user interface designed around user-friendliness with extra features that are not included in the standard UI.","logo_square_url":null,"directories":["ElvUI","ElvUI_Libraries","ElvUI_Options"]},
          {"id":-1,"slug":"tukui","author":"Tukz","name":"Tukui","url":"https://api.tukui.org/v1/download/tukui/token","version":"20.463","changelog_url":null,"patch":["11.1.7","5.5.0","1.15.7"],"last_update":"2025-12-05","web_url":"https://tukui.org/tukui","small_desc":"Minimalistic and lightweight World of Warcraft user interface.","logo_square_url":null,"directories":["Tukui"]}
        ]
        """;

    public const string Products = """
        {"products":[
          {"code":"wow_classic_beta","name":null,"icon_url":null,"events":[
            {"name":"Server event","starts_at":1893000000000,"ends_at":1893200000000,"headline":false,"upcoming_text":"Opens in {time}","active_text":"Live now"},
            {"name":"Launch","starts_at":1893456000000,"ends_at":null,"headline":true,"upcoming_text":null,"active_text":null}
          ]},
          {"code":"wowf","name":null,"icon_url":null,"events":[
            {"name":"Launch","starts_at":1893456000000,"ends_at":null,"headline":true}
          ]},
          {"code":"wow","events":[]},
          {"code":"broken","events":"nope"}
        ]}
        """;

    public string MeBody { get; set; } = Me("steward", "sync", "roster", "addons", "guides");

    public string ProductsBody { get; set; } = Products;

    public HttpMessageHandler Handler() => new Routed(this);

    private (HttpStatusCode Status, string Body)? Route(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (Down && request.RequestUri.Host == "api.hoobi.io")
        {
            return (HttpStatusCode.BadGateway, "");
        }

        if (request.RequestUri.Host == "api.tukui.org")
        {
            return path == "/v1/addons" ? (HttpStatusCode.OK, TukUiList) : (HttpStatusCode.NotFound, "");
        }

        if (path.EndsWith("/api/me", StringComparison.Ordinal))
        {
            return (HttpStatusCode.OK, MeBody);
        }

        if (path.EndsWith("/api/banners", StringComparison.Ordinal))
        {
            return (HttpStatusCode.OK, """{"banners":[]}""");
        }

        if (path.EndsWith("/api/app/products", StringComparison.Ordinal))
        {
            return (HttpStatusCode.OK, ProductsBody);
        }

        if (path.EndsWith("/latest-release.json", StringComparison.Ordinal) || path.EndsWith("/latest-pre-release.json", StringComparison.Ordinal))
        {
            var channel = path.EndsWith("/latest-release.json", StringComparison.Ordinal) ? "release" : "pre-release";
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var key = request.RequestUri.Host == "addon.test" ? segments[0] : segments[^3];
            if (Versions.TryGetValue(key, out var versions) && (channel == "release" ? versions.Release : versions.PreRelease) is { } version)
            {
                return (HttpStatusCode.OK, Manifest(version, channel));
            }

            return (HttpStatusCode.NotFound, "");
        }

        if (path.EndsWith("/api/addons/curseforge/discover", StringComparison.Ordinal))
        {
            return (HttpStatusCode.OK, $$"""{"popular":[{{Results("Popular", 12)}}],"recentlyUpdated":[]}""");
        }

        if (path.EndsWith("/api/addons/curseforge/search", StringComparison.Ordinal))
        {
            var query = Uri.UnescapeDataString(request.RequestUri.Query.Split("q=")[^1]);
            return (HttpStatusCode.OK, $$"""{"results":[{{Results(query, 30)}}]}""");
        }

        if (path.EndsWith("/api/addons/curseforge/match", StringComparison.Ordinal))
        {
            return (HttpStatusCode.OK, "[]");
        }

        if (path.EndsWith("/api/addons/curseforge/3358/files/9181264", StringComparison.Ordinal))
        {
            return (HttpStatusCode.OK, $$"""
                {"modId":3358,"name":"DBM - Deadly Boss Mods (DBM-Core)","iconUrl":null,"websiteUrl":"https://www.curseforge.com/wow/addons/deadly-boss-mods","allowDistribution":true,
                 "file":{{Manifest("12.1.14", "release")}},"releaseType":1,"gameVersionTypeIds":[88568],"author":"MysticalOS"}
                """);
        }

        if (path.EndsWith("/api/admin/1/roster", StringComparison.Ordinal))
        {
            return (HttpStatusCode.OK, """{"members":[],"statuses":[],"origins":[]}""");
        }

        if (path.EndsWith("/api/admin/1/members", StringComparison.Ordinal))
        {
            return (HttpStatusCode.OK, """{"members":[]}""");
        }

        if (path.EndsWith("/api/sync/1/characters", StringComparison.Ordinal))
        {
            return (HttpStatusCode.OK, """{"accepted":1,"rejected":[]}""");
        }

        if (path.EndsWith("/api/guild/1/roster", StringComparison.Ordinal))
        {
            return (HttpStatusCode.OK, """{"people":[],"characters":[]}""");
        }

        return null;
    }

    private static string Manifest(string version, string channel) =>
        $$"""{"version":"{{version}}","zip":"{{channel}}.zip","sha256":"00","size":1,"released":"2026-10-12T09:30:00+00:00","notes":["Fixed the roster export on {{channel}}","Faster startup"]}""";

    private static string Results(string prefix, int count) => string.Join(",", Enumerable.Range(1, count).Select(id =>
        $$"""{"id":{{9000 + id}},"name":"{{prefix}} addon {{id}}","summary":"Summary {{id}}","author":"Author","iconUrl":null,"websiteUrl":"https://www.curseforge.com/wow/addons/x{{id}}","latestVersion":"v1.{{id}}"}"""));

    private sealed class Routed(FakeApi api) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            api.Requests.Enqueue($"{request.Method} {uri}");
            if (uri.Host is not ("api.hoobi.io" or "addon.test" or "api.tukui.org"))
            {
                throw new HttpRequestException($"Network access to {uri.Host} is disabled in the UI path suite.");
            }

            var routed = api.Route(request);
            if (routed is null)
            {
                api.Unrouted.Enqueue($"{request.Method} {uri.AbsolutePath}");
            }

            var (status, body) = routed ?? (HttpStatusCode.NotFound, "");
            return Task.FromResult(new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
