using System.Runtime.InteropServices;

using Steward.Core;

namespace Steward.App.Services;

public sealed class DiscordImage(HttpClient httpClient, IUiPlatform platform)
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
        DownloadAsync(avatarUrl, PersonAvatars.PersonSize, cancellationToken);

    public Task<AvatarImage?> DownloadAsync(string avatarUrl, int size, CancellationToken cancellationToken) =>
        Uri.TryCreate(avatarUrl, UriKind.Absolute, out var source)
            ? FetchAsync(PersonAvatars.RequestUri(source, size), cancellationToken)
            : Task.FromResult<AvatarImage?>(null);

    private async Task<AvatarImage?> FetchAsync(string requestUri, CancellationToken cancellationToken)
    {
        try
        {
            var png = await httpClient.GetByteArrayAsync(requestUri, cancellationToken).ConfigureAwait(false);
            var pixels = await platform.DecodeBgraAsync(png, null).ConfigureAwait(false);
            return new AvatarImage(requestUri, pixels.Width, pixels.Height, pixels.Bgra);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or COMException or ArgumentException)
        {
            return null;
        }
    }
}
