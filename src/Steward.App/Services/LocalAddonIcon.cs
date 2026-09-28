using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;

using Steward.Core;

using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Steward.App.Services;

public static class LocalAddonIcon
{
    private static readonly ConcurrentDictionary<string, (DateTime Stamp, DecodedImage? Image)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<ImageSource?> LoadAsync(string addOnsPath, string folderName)
    {
        var folder = Path.Combine(addOnsPath, folderName);
        var stamp = Directory.GetLastWriteTimeUtc(folder);
        if (!Cache.TryGetValue(folder, out var cached) || cached.Stamp != stamp)
        {
            cached = (stamp, await Task.Run(() => AddonIcon.TryLoad(addOnsPath, folderName)).ConfigureAwait(true));
            Cache[folder] = cached;
        }

        if (cached.Image is not { } image)
        {
            return null;
        }

        var bitmap = new WriteableBitmap(image.Width, image.Height);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            await stream.WriteAsync(Premultiplied(image.Bgra)).ConfigureAwait(true);
        }

        bitmap.Invalidate();
        return bitmap;
    }

    // WriteableBitmap expects premultiplied alpha; the decoder hands back straight BGRA.
    private static byte[] Premultiplied(byte[] bgra)
    {
        var result = new byte[bgra.Length];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var alpha = bgra[i + 3];
            result[i] = (byte)(bgra[i] * alpha / 255);
            result[i + 1] = (byte)(bgra[i + 1] * alpha / 255);
            result[i + 2] = (byte)(bgra[i + 2] * alpha / 255);
            result[i + 3] = alpha;
        }

        return result;
    }
}
