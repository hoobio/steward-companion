using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;

namespace Steward.Core;

public sealed class AddonUpdater
{
    private readonly HttpClient _httpClient;

    public AddonUpdater(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<AddonRelease> GetLatestAsync(ManagedAddon addon, string channel, CancellationToken cancellationToken)
    {
        var manifestUri = ManifestUri(addon, channel);

        using var request = new HttpRequestMessage(HttpMethod.Get, manifestUri);
        // The Static Web App route for this manifest has unconfirmed cache headers, so force
        // a revalidation here rather than trust an intermediate cache to serve a fresh copy.
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var release = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.AddonRelease, cancellationToken)
            .ConfigureAwait(false);

        return release ?? throw new HttpRequestException($"GET {manifestUri} returned an empty body");
    }

    public async Task InstallAsync(
        ManagedAddon addon,
        string channel,
        AddonRelease release,
        string addOnsPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var manifestUri = ManifestUri(addon, channel);
        var zipUri = new Uri(manifestUri, release.Zip);
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");

        try
        {
            await DownloadAsync(zipUri, tempZipPath, release.Size, progress, cancellationToken).ConfigureAwait(false);
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
        new(new Uri(addon.ManifestBaseUrl), $"latest-{channel}.json");

    private async Task DownloadAsync(
        Uri zipUri,
        string destinationPath,
        long expectedSize,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient
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
