using System.Collections.Concurrent;
using System.Diagnostics;

using Steward.Core;

namespace Steward.App.Services;

public static class ManifestIcon
{
    private static readonly ConcurrentDictionary<string, Task> Refreshes = new(StringComparer.OrdinalIgnoreCase);
    private const int DisplayPixels = 36;
    private static readonly ConcurrentDictionary<(string Path, DateTime Stamp), object> Decoded = new();

    public static HttpClient Http { get; set; } = new();

    public static string CacheFolder { get; set; } = Path.Combine(LocalData.Folder, "icons");

    public static object For(IUiPlatform platform, ManagedAddon addon)
    {
        ArgumentNullException.ThrowIfNull(addon);

        return For(platform, addon.Id, addon.IconUri);
    }

    public static object For(IUiPlatform platform, string cacheKey, Uri source)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(cacheKey);
        ArgumentNullException.ThrowIfNull(source);

        var path = Path.Combine(CacheFolder, string.Join('_', cacheKey.Split(Path.GetInvalidFileNameChars())) + ".png");
        if (!source.IsFile)
        {
            Refreshes.GetOrAdd(cacheKey, _ => Task.Run(() => RefreshAsync(source, path)));
        }

        try
        {
            var key = (path, File.GetLastWriteTimeUtc(path));
            if (!Decoded.TryGetValue(key, out var bitmap))
            {
                bitmap = platform.ImageFromBytes(File.ReadAllBytes(path), DisplayPixels, () => Decoded.TryRemove(key, out _));
                Decoded[key] = bitmap;
            }

            return bitmap;
        }
        catch (IOException)
        {
            return platform.ImageFromUri(source, DisplayPixels);
        }
    }

    private static async Task RefreshAsync(Uri source, string path)
    {
        try
        {
            using var response = await Http.GetAsync(source).ConfigureAwait(false);
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
