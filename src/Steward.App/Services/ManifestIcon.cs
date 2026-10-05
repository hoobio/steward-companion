using System.Collections.Concurrent;
using System.Diagnostics;

using Steward.Core;

using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Steward.App.Services;

public static class ManifestIcon
{
    private static readonly HttpClient HttpClient = new();
    private static readonly ConcurrentDictionary<string, Task> Refreshes = new(StringComparer.OrdinalIgnoreCase);
    private const int DisplayPixels = 64;
    private static readonly ConcurrentDictionary<(string Path, DateTime Stamp), BitmapImage> Decoded = new();
    private static readonly string CacheFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steward", "icons");

    public static ImageSource For(ManagedAddon addon)
    {
        ArgumentNullException.ThrowIfNull(addon);

        return For(addon.Id, addon.IconUri);
    }

    public static ImageSource For(string cacheKey, Uri source)
    {
        ArgumentNullException.ThrowIfNull(cacheKey);
        ArgumentNullException.ThrowIfNull(source);

        var path = Path.Combine(CacheFolder, string.Join('_', cacheKey.Split(Path.GetInvalidFileNameChars())) + ".png");
        Refreshes.GetOrAdd(cacheKey, _ => Task.Run(() => RefreshAsync(source, path)));

        try
        {
            var key = (path, File.GetLastWriteTimeUtc(path));
            if (!Decoded.TryGetValue(key, out var bitmap))
            {
                bitmap = new BitmapImage { DecodePixelWidth = DisplayPixels };
                using var stream = new MemoryStream(File.ReadAllBytes(path));
                bitmap.SetSource(stream.AsRandomAccessStream());
                Decoded[key] = bitmap;
            }

            return bitmap;
        }
        catch (IOException)
        {
            return new BitmapImage(source) { DecodePixelWidth = DisplayPixels };
        }
    }

    private static async Task RefreshAsync(Uri source, string path)
    {
        try
        {
            using var response = await HttpClient.GetAsync(source).ConfigureAwait(false);
            // A Static Web App fallback answers a missing path with index.html and a 200.
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
            {
                return;
            }

            Directory.CreateDirectory(CacheFolder);
            var temp = path + ".tmp";
            await File.WriteAllBytesAsync(temp, await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false)).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Icon refresh for {source} failed: {ex.Message}");
        }
    }
}
