using Steward.Core;

namespace Steward.App.Services;

public sealed class GigagrugGuildSyncApi(GigagrugClient client) : IGuildSyncApi
{
    public Task<SyncServerState> GetStateAsync(CancellationToken ct) =>
        throw new NotSupportedException(
            "GigagrugGuildSyncApi only pulls the guild roster and Discord member list; it has no push-direction sync state to read.");

    public Task<SyncPushResult> PushAsync(string dataset, SyncPayload payload, IProgress<double>? progress, CancellationToken ct) =>
        throw new NotSupportedException(
            "GigagrugGuildSyncApi is pull-only: gigagrug -> app -> game. Nothing is pushed.");

    public async Task<SyncPayload> PullAsync(CancellationToken ct)
    {
        var me = await client.GetMeAsync(ct).ConfigureAwait(false);
        if (me.Guilds.Count == 0)
        {
            throw new InvalidOperationException("The signed-in user has no guild to sync from.");
        }

        var guildId = me.Guilds[0].Id;
        var members = await client.GetGuildRosterAsync(guildId, ct).ConfigureAwait(false);
        var discord = await client.GetDiscordMembersAsync(guildId, ct).ConfigureAwait(false);

        return new SyncPayload(DateTimeOffset.Now, null, [], [], [], members, discord);
    }
}
