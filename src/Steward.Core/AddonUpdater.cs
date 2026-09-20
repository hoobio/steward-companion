using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;

namespace Steward.Core;

public sealed class AddonUpdater
{
    private readonly HttpClient _httpClient;

    public AddonUpdater(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<AddonRelease?> GetLatestAsync(ManagedAddon addon, string channel, CancellationToken cancellationToken)
    {
        if (addon.GitHubRepo is { } repo)
        {
            return addon.Channels.Contains(channel, StringComparer.OrdinalIgnoreCase)
                ? await GitHubReleases.GetLatestAsync(_httpClient, repo, ".zip", channel, cancellationToken).ConfigureAwait(false)
                : null;
        }

        var manifestUri = ManifestUri(addon, channel);

        using var request = new HttpRequestMessage(HttpMethod.Get, manifestUri);
        // The Static Web App route for this manifest has unconfirmed cache headers.
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // A channel with no releases publishes the literal JSON `null`, so null here means an empty channel rather than a fault.
        return await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.AddonRelease, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, AddonRelease?>> ProbeChannelsAsync(
        ManagedAddon addon, IReadOnlyList<string> channels, CancellationToken cancellationToken)
    {
        var releases = await Task.WhenAll(channels.Select(channel => ProbeChannelAsync(addon, channel, cancellationToken)))
            .ConfigureAwait(false);

        var result = new Dictionary<string, AddonRelease?>(channels.Count);
        for (var i = 0; i < channels.Count; i++)
        {
            result[channels[i]] = releases[i];
        }

        return result;
    }

    private async Task<AddonRelease?> ProbeChannelAsync(ManagedAddon addon, string channel, CancellationToken cancellationToken)
    {
        try
        {
            return await GetLatestAsync(addon, channel, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task InstallAsync(
        ManagedAddon addon,
        string channel,
        AddonRelease release,
        string addOnsPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var zipUri = addon.ManifestBaseUrl is null
            ? new Uri(release.Zip)
            : new Uri(ManifestUri(addon, channel), release.Zip);
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");

        try
        {
            await DownloadAsync(_httpClient, zipUri, tempZipPath, release.Size, progress, cancellationToken).ConfigureAwait(false);
            await VerifyChecksumAsync(tempZipPath, release.Sha256, cancellationToken).ConfigureAwait(false);
            RemoveExistingInstall(addOnsPath, addon.FolderName);
            ExtractZip(tempZipPath, addOnsPath);
        }
        finally
        {
            if (File.Exists(tempZipPath))
            {
                File.Delete(tempZipPath);
            }
        }
    }

    private static Uri ManifestUri(ManagedAddon addon, string channel) =>
        new(new Uri(addon.ManifestBaseUrl ?? throw new InvalidOperationException($"{addon.Id} has no ManifestBaseUrl")), $"latest-{channel}.json");

    internal static async Task DownloadAsync(
        HttpClient httpClient,
        Uri zipUri,
        string destinationPath,
        long expectedSize,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient
            .GetAsync(zipUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = File.Create(destinationPath);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            totalRead += bytesRead;
            if (expectedSize > 0)
            {
                progress?.Report((double)totalRead / expectedSize);
            }
        }
    }

    internal static async Task VerifyChecksumAsync(
        string filePath,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        string actualHash;
        await using (var stream = File.OpenRead(filePath))
        {
            actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        }

        if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(filePath);
            throw new InvalidOperationException(
                $"SHA-256 mismatch: expected {expectedSha256}, got {actualHash}");
        }
    }

    internal static void RemoveExistingInstall(string addOnsPath, string folderName)
    {
        var existingPath = Path.Combine(addOnsPath, folderName);
        if (!Directory.Exists(existingPath))
        {
            return;
        }

        var tocFileName = $"{folderName}.toc";
        if (!File.Exists(Path.Combine(existingPath, tocFileName)))
        {
            throw new InvalidOperationException(
                $"{existingPath} exists but has no {tocFileName}; refusing to delete it");
        }

        foreach (var file in Directory.EnumerateFiles(existingPath, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(existingPath, recursive: true);
    }

    internal static void ExtractZip(string zipPath, string addOnsPath)
    {
        var destinationRoot = Path.GetFullPath(Path.TrimEndingDirectorySeparator(addOnsPath) + Path.DirectorySeparatorChar);

        using var archive = ZipFile.OpenRead(zipPath);
        var fileEntries = archive.Entries.Where(entry => !entry.FullName.EndsWith('/')).ToList();
        var destinationPaths = new string[fileEntries.Count];

        for (var i = 0; i < fileEntries.Count; i++)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, fileEntries[i].FullName));
            if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Zip entry {fileEntries[i].FullName} escapes {addOnsPath}");
            }

            destinationPaths[i] = destinationPath;
        }

        for (var i = 0; i < fileEntries.Count; i++)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPaths[i])!);
            fileEntries[i].ExtractToFile(destinationPaths[i], overwrite: true);
        }
    }
}
