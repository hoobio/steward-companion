using Steward.Core;
using Steward.Core.Diagnostics;

using Microsoft.Extensions.Logging;

using Windows.Graphics.Imaging;

namespace Steward.App.Services;

public static class InGameIcon
{
    public static async Task EnsureAsync(AddonUpdater updater, string addOnsPath, ManagedAddon addon, ILogger logger)
    {
        try
        {
            if (await Task.Run(() => GeneratedAddonIcon.Plan(addOnsPath, addon.FolderName)).ConfigureAwait(false) is not { } plan)
            {
                return;
            }

            var texture = plan.WriteTexture ? await LoadTextureAsync(updater, addon.IconUri).ConfigureAwait(false) : null;
            await Task.Run(() => GeneratedAddonIcon.Apply(addOnsPath, addon.FolderName, plan, texture)).ConfigureAwait(false);
            logger.Info($"Wrote an in-game icon for {addon.Id} into {addOnsPath}");
        }
        catch (Exception ex)
        {
            logger.Warn(ex, $"In-game icon for {addon.Id} in {addOnsPath} skipped");
        }
    }

    private static async Task<byte[]> LoadTextureAsync(AddonUpdater updater, Uri iconUri)
    {
        var bytes = await updater.GetIconAsync(iconUri, CancellationToken.None).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes).AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            new BitmapTransform
            {
                ScaledWidth = GeneratedAddonIcon.Size,
                ScaledHeight = GeneratedAddonIcon.Size,
                InterpolationMode = BitmapInterpolationMode.Fant,
            },
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        return AvatarTga.Encode(new AvatarImage(iconUri.ToString(), GeneratedAddonIcon.Size, GeneratedAddonIcon.Size, pixels.DetachPixelData()));
    }
}
