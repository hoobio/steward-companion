using Steward.Core;
using Steward.Core.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Steward.App.Services;

public static class InGameIcon
{
    public static async Task EnsureAsync(IUiPlatform platform, AddonUpdater updater, string addOnsPath, ManagedAddon addon, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(addon);
        if (addon.IconNeedsSession)
        {
            return;
        }

        try
        {
            if (await Task.Run(() => GeneratedAddonIcon.Plan(addOnsPath, addon.FolderName)).ConfigureAwait(false) is not { } plan)
            {
                return;
            }

            var texture = plan.WriteTexture ? await LoadTextureAsync(platform, updater, addon.IconUri).ConfigureAwait(false) : null;
            await Task.Run(() => GeneratedAddonIcon.Apply(addOnsPath, addon.FolderName, plan, texture)).ConfigureAwait(false);
            logger.Info($"Wrote an in-game icon for {addon.Id} into {addOnsPath}");
        }
        catch (Exception ex)
        {
            logger.Warn(ex, $"In-game icon for {addon.Id} in {addOnsPath} skipped");
        }
    }

    private static async Task<byte[]> LoadTextureAsync(IUiPlatform platform, AddonUpdater updater, Uri iconUri)
    {
        var bytes = await updater.GetIconAsync(iconUri, CancellationToken.None).ConfigureAwait(false);
        var pixels = await platform.DecodeBgraAsync(bytes, GeneratedAddonIcon.Size).ConfigureAwait(false);
        return AvatarTga.Encode(new AvatarImage(iconUri.ToString(), GeneratedAddonIcon.Size, GeneratedAddonIcon.Size, pixels.Bgra));
    }
}
