using System.Collections.Concurrent;
using System.Text.Json;
using Steward.Core;

namespace Steward.App.Services;

public sealed class GigagrugGuildSyncApi(GigagrugClient client, AppStateStore stateStore, DiscordImage images)
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<CatalogueRecipe>> NoCatalogue = new Dictionary<string, IReadOnlyList<CatalogueRecipe>>();

    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<CatalogueRecipe>>> _lastCatalogue = new();

    public async Task<SyncPayload> PullAsync(string? guildId, CancellationToken ct)
    {
        var me = await client.GetMeAsync(ct).ConfigureAwait(false);
        var guild = guildId is null
            ? me.ResolveGuild(stateStore.Load().GuildId)
            : me.Guilds.FirstOrDefault(candidate => candidate.Id == guildId);
        guildId ??= guild?.Id
            ?? throw new InvalidOperationException("The signed-in user has no guild to sync from.");

        var (members, statuses, origins) = await client.GetGuildRosterAsync(guildId, ct).ConfigureAwait(false);
        var discord = await client.GetDiscordMembersAsync(guildId, ct).ConfigureAwait(false);
        var icon = await images.LoadAsync(guild?.IconUrl, ct).ConfigureAwait(false);
        var catalogue = await TryGetCatalogueAsync(me, guildId, ct).ConfigureAwait(false);

        return new SyncPayload(DateTimeOffset.Now, null, [], [], [], members, discord, statuses) { Avatar = icon, Origins = origins, Catalogue = catalogue };
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<CatalogueRecipe>>> TryGetCatalogueAsync(AdminMe me, string guildId, CancellationToken ct)
    {
        if (!GigagrugClient.EffectiveFeatures(me).Contains(GigagrugClient.SyncFeature))
        {
            return NoCatalogue;
        }

        var key = $"{me.User.Id}|{guildId}";
        try
        {
            var catalogue = await client.GetRecipeCatalogueAsync(guildId, ct).ConfigureAwait(false);
            _lastCatalogue[key] = catalogue;
            return catalogue;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or SessionExpiredException or TaskCanceledException)
        {
            // a failed fetch (404 or timeout included) never blocks the roster write; that user's last catalogue for this guild is written instead.
            return _lastCatalogue.GetValueOrDefault(key) ?? NoCatalogue;
        }
    }
}
