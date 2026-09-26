using System.Text.Json;
using Steward.Core;

namespace Steward.App.Services;

public sealed class GigagrugGuildSyncApi(GigagrugClient client, AppStateStore stateStore, DiscordImage images) : IGuildSyncApi
{
    private IReadOnlyDictionary<string, IReadOnlyList<CatalogueRecipe>> _lastCatalogue = new Dictionary<string, IReadOnlyList<CatalogueRecipe>>();

    public Task<SyncServerState> GetStateAsync(CancellationToken ct) =>
        throw new NotSupportedException(
            "GigagrugGuildSyncApi only pulls the guild roster and Discord member list; it has no push-direction sync state to read.");

    public Task<SyncPushResult> PushAsync(string dataset, SyncPayload payload, IProgress<double>? progress, CancellationToken ct) =>
        throw new NotSupportedException(
            "GigagrugGuildSyncApi is pull-only: gigagrug -> app -> game. Nothing is pushed.");

    public Task<SyncPayload> PullAsync(CancellationToken ct) => PullAsync(guildId: null, ct);

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
            return new Dictionary<string, IReadOnlyList<CatalogueRecipe>>();
        }

        try
        {
            _lastCatalogue = await client.GetRecipeCatalogueAsync(guildId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or SessionExpiredException or TaskCanceledException)
        {
            // a catalogue fetch failure (404 or a timeout included) never blocks the roster write; the last successfully fetched catalogue is kept instead.
        }

        return _lastCatalogue;
    }
}
