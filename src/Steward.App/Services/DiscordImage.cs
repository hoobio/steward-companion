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

        var requestUri = PersonAvatars.RequestUri(source);
        if (_cached is { } cached && string.Equals(cached.SourceUrl, requestUri, StringComparison.Ordinal))
        {
            return cached;
        }

        var image = await DownloadAsync(requestUri, cancellationToken).ConfigureAwait(false);
        if (image is not null)
        {
            _cached = image;
        }

        return image;
    }

    public Task<AvatarImage?> DownloadAsync(string avatarUrl, CancellationToken cancellationToken) =>
        Uri.TryCreate(avatarUrl, UriKind.Absolute, out var source)
            ? FetchAsync(PersonAvatars.RequestUri(source), cancellationToken)
            : Task.FromResult<AvatarImage?>(null);

    private async Task<AvatarImage?> FetchAsync(string requestUri, CancellationToken cancellationToken)
    {
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

            return new AvatarImage(requestUri, (int)decoder.PixelWidth, (int)decoder.PixelHeight, pixels.DetachPixelData());
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or COMException or ArgumentException)
        {
            return null;
        }
    }
}
