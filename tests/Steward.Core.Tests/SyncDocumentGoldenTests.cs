using System.Net;
using System.Text;
using System.Text.Json;

namespace Steward.Core.Tests;

public sealed class SyncDocumentGoldenTests
{
    private const string GoldenName = "steward-sync-officer.golden.lua";

    private static readonly JsonElement Inputs =
        JsonDocument.Parse(File.ReadAllText(Fixture("steward-sync-document.json"))).RootElement;

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private sealed class FixtureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var part = request.RequestUri!.AbsolutePath switch
            {
                "/guild/api/admin/1/roster" => "adminRoster",
                "/guild/api/admin/1/members" => "adminMembers",
                "/guild/api/admin/1/recipes/catalogue" => "adminCatalogue",
                "/guild/api/guild/1/roster" => "memberRoster",
                "/guild/api/guild/1/professions" => "memberProfessions",
                "/guild/api/guild/1/gear/sets" => "gearSets",
                var path => throw new InvalidOperationException($"unexpected request {path}"),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Inputs.GetProperty(part).GetRawText(), Encoding.UTF8, "application/json"),
            });
        }
    }

    private static async Task<SyncPayload> TypedOfficerPayloadAsync()
    {
        var client = new StewardClient(new HttpClient(new FixtureHandler()), "https://api.example.com/guild", "Steward/1.0.0 (test)");
        var ct = TestContext.Current.CancellationToken;
        var (members, statuses, origins, rankTargets, rankLocked, kos, attunements) = await client.GetGuildRosterAsync("1", ct);
        var discord = await client.GetDiscordMembersAsync("1", ct);
        var catalogue = await client.GetRecipeCatalogueAsync("1", ct);
        var gearSets = await client.GetGearSetsAsync("1", ct);
        var roster = await client.GetMemberRosterAsync("1", ct);
        var professions = await client.GetMemberProfessionsAsync("1", ct);
        var me = Inputs.GetProperty("me");
        return new SyncPayload(DateTimeOffset.FromUnixTimeSeconds(1760000000), null, [], [], [], members, discord, statuses)
        {
            Origins = origins,
            Catalogue = catalogue,
            Me = new SyncMe(me.GetProperty("id").GetString()!, me.GetProperty("role").GetString(), [.. me.GetProperty("features").EnumerateArray().Select(f => f.GetString()!)]),
            RankTargets = rankTargets,
            RankLocked = rankLocked,
            Kos = kos,
            Attunements = attunements,
            GearSets = gearSets,
            Directory = new SyncDirectory(roster.People, roster.Characters, professions.Professions),
        };
    }

    [Fact]
    public async Task TheTypedRenderer_FromTheFixtureInputs_MatchesTheGolden()
    {
        var rendered = StewardSyncFile.Render(await TypedOfficerPayloadAsync()).TrimEnd('\r', '\n');
        if (Environment.GetEnvironmentVariable("STEWARD_WRITE_GOLDEN") is { Length: > 0 } target)
        {
            File.WriteAllText(target, rendered, new UTF8Encoding(false));
        }

        Assert.Equal(File.ReadAllText(Fixture(GoldenName), new UTF8Encoding(false)), rendered);
    }
}
