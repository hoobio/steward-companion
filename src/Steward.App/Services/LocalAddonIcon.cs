using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;

using Steward.Core;
using Steward.Core.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Steward.App.Services;

public static class LocalAddonIcon
{
    private const int DisplayPixels = 75;

    private static readonly ConcurrentDictionary<string, (DateTime Stamp, DecodedImage? Image)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<ImageSource?> LoadAsync(string addOnsPath, string folderName, ILogger logger)
    {
        try
        {
            var folder = Path.Combine(addOnsPath, folderName);
            var image = await Task.Run(() =>
            {
                var stamp = Directory.GetLastWriteTimeUtc(folder);
                if (!Cache.TryGetValue(folder, out var cached) || cached.Stamp != stamp)
                {
                    var decoded = AddonIcon.TryLoad(addOnsPath, folderName);
                    cached = (stamp, decoded is { Width: > 0, Height: > 0 } ? PremultipliedDownscaled(decoded) : null);
                    Cache[folder] = cached;
                }

                return cached.Image;
            }).ConfigureAwait(true);

            if (image is null)
            {
                return null;
            }

            var bitmap = new WriteableBitmap(image.Width, image.Height);
            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                await stream.WriteAsync(image.Bgra).ConfigureAwait(true);
            }

            bitmap.Invalidate();
            return bitmap;
        }
        catch (Exception ex)
        {
            logger.Warn(ex, $"Could not load the icon for {folderName} in {addOnsPath}");
            return null;
        }
    }

    // WriteableBitmap expects premultiplied alpha; the decoder hands back straight BGRA.
    private static DecodedImage PremultipliedDownscaled(DecodedImage image)
    {
        var factor = Math.Max(1, (Math.Max(image.Width, image.Height) + DisplayPixels - 1) / DisplayPixels);
        var width = (image.Width + factor - 1) / factor;
        var height = (image.Height + factor - 1) / factor;
        var result = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int b = 0, g = 0, r = 0, a = 0, count = 0;
                for (var sy = y * factor; sy < Math.Min(image.Height, (y + 1) * factor); sy++)
                {
                    for (var sx = x * factor; sx < Math.Min(image.Width, (x + 1) * factor); sx++)
                    {
                        var i = ((sy * image.Width) + sx) * 4;
                        var alpha = image.Bgra[i + 3];
                        b += image.Bgra[i] * alpha / 255;
                        g += image.Bgra[i + 1] * alpha / 255;
                        r += image.Bgra[i + 2] * alpha / 255;
                        a += alpha;
                        count++;
                    }
                }

                var o = ((y * width) + x) * 4;
                result[o] = (byte)(b / count);
                result[o + 1] = (byte)(g / count);
                result[o + 2] = (byte)(r / count);
                result[o + 3] = (byte)(a / count);
            }
        }

        return new DecodedImage(width, height, result);
    }
}
