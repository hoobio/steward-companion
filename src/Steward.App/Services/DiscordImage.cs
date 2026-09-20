using System.Runtime.InteropServices;

using Steward.Core;

using Windows.Graphics.Imaging;

namespace Steward.App.Services;

public sealed class DiscordImage(HttpClient httpClient)
{
    private AvatarImage? _cached;

    public async Task<AvatarImage?> LoadAsync(string? avatarUrl, CancellationToken cancellationToken)
    {
        if (avatarUrl is null || !Uri.TryCreate(avatarUrl, UriKind.Absolute, out var source))
        {
            return null;
        }

        var requestUri = RequestUri(source);
        if (_cached is { } cached && string.Equals(cached.SourceUrl, requestUri, StringComparison.Ordinal))
        {
            return cached;
        }

        try
        {
            var png = await httpClient.GetByteArrayAsync(requestUri, cancellationToken).ConfigureAwait(false);
            using var stream = new MemoryStream(png).AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Straight,
                new BitmapTransform(),
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            _cached = new AvatarImage(requestUri, (int)decoder.PixelWidth, (int)decoder.PixelHeight, pixels.DetachPixelData());
            return _cached;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or COMException or ArgumentException)
        {
            return null;
        }
    }

    private static string RequestUri(Uri source) =>
        new UriBuilder(source) { Path = Path.ChangeExtension(source.AbsolutePath, ".png"), Query = "size=64" }.Uri.ToString();
}
