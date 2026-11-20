using Steward.Core;
using Steward.Core.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Steward.App.Services;

public sealed class StewardGuildSyncApi(StewardClient client, AppStateStore stateStore, DiscordImage images, ILogger<StewardGuildSyncApi> logger)
{
    public async Task<SyncPayload> PullAsync(string? guildId, IReadOnlyCollection<WowInstall> installs, SyncPayload? previous, CancellationToken ct)
    {
        var payload = await client.GetSyncDocumentAsync(guildId, ct).ConfigureAwait(false);
        logger.Info(
            $"Sync document pulled for {guildId ?? "no guild"}: {payload.Entries.Count} key(s), {payload.Images.People.Count} person and {payload.Images.Discord.Count} Discord image(s)");
        return await LoadImagesAsync(payload, installs, previous, ct).ConfigureAwait(false);
    }

    public async Task<SyncPayload> LoadImagesAsync(SyncPayload payload, IReadOnlyCollection<WowInstall> installs, SyncPayload? previous, CancellationToken ct)
    {
        var icon = await images.LoadAsync(payload.Images.Guild, ct).ConfigureAwait(false);
        IReadOnlyDictionary<string, AvatarImage> avatars;
        try
        {
            avatars = await PersonAvatars.LoadAsync(payload.Images, installs, stateStore, previous?.Avatars, images.DownloadAsync, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warn(ex, "Avatar download failed, keeping the avatars already loaded");
            avatars = previous?.Avatars ?? new Dictionary<string, AvatarImage>();
        }

        return payload with { Avatar = icon, Avatars = avatars };
    }
}
