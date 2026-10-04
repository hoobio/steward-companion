using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;

using Steward.Core.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Steward.Core;

public sealed class AddonUpdater
{
    internal static readonly Lock AddOnsWriteLock = new();

    private readonly HttpClient _httpClient;
    private readonly HttpClient? _sessionClient;
    private readonly ILogger _logger;
    private readonly Func<CurseForgeDownloadFailure, CancellationToken, Task>? _reportDownloadFailure;
    private readonly ConcurrentDictionary<string, byte> _reportedFailures = new();

    public AddonUpdater(
        HttpClient httpClient,
        ILogger<AddonUpdater>? logger = null,
        HttpClient? sessionClient = null,
        Func<CurseForgeDownloadFailure, CancellationToken, Task>? reportDownloadFailure = null)
    {
        _httpClient = httpClient;
        _sessionClient = sessionClient;
        _reportDownloadFailure = reportDownloadFailure;
        _logger = logger ?? NullLogger<AddonUpdater>.Instance;
    }

    public async Task<AddonRelease?> GetLatestAsync(ManagedAddon addon, string channel, CancellationToken cancellationToken, bool noCache = false)
    {
        ArgumentNullException.ThrowIfNull(addon);
        var client = addon.Source == CurseForgeAddons.Source ? _sessionClient ?? _httpClient : _httpClient;
        return await FetchManifestAsync(client, ManifestUri(addon, channel), cancellationToken, noCache).ConfigureAwait(false);
    }

    public Task<byte[]> GetIconAsync(Uri iconUri, CancellationToken cancellationToken) =>
        _httpClient.GetByteArrayAsync(iconUri, cancellationToken);

    public static async Task<AddonRelease?> FetchManifestAsync(HttpClient httpClient, Uri manifestUri, CancellationToken cancellationToken, bool noCache = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        using var request = new HttpRequestMessage(HttpMethod.Get, manifestUri);
        if (noCache)
        {
            // steward-server serves a CurseForge manifest up to 15 minutes old, or 1 minute old on no-cache.
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        }

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SessionExpiredException();
        }

        response.EnsureSuccessStatusCode();

        // A channel with no releases publishes the literal JSON `null`, so null here means an empty channel rather than a fault.
        return await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.AddonRelease, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, AddonRelease?>> ProbeChannelsAsync(
        ManagedAddon addon, IReadOnlyList<string> channels, CancellationToken cancellationToken, bool noCache = false)
    {
        var releases = await Task.WhenAll(channels.Select(channel => ProbeChannelAsync(addon, channel, noCache, cancellationToken)))
            .ConfigureAwait(false);

        var result = new Dictionary<string, AddonRelease?>(channels.Count);
        for (var i = 0; i < channels.Count; i++)
        {
            result[channels[i]] = releases[i];
        }

        return result;
    }

    private async Task<AddonRelease?> ProbeChannelAsync(ManagedAddon addon, string channel, bool noCache, CancellationToken cancellationToken)
    {
        try
        {
            return await GetLatestAsync(addon, channel, cancellationToken, noCache).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> InstallAsync(
        ManagedAddon addon,
        string channel,
        AddonRelease release,
        string addOnsPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        _logger.Info($"Installing {addon.Id} {channel} {release.Version} into {addOnsPath}");
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        IReadOnlyList<string> folders;
        try
        {
            try
            {
                if (!release.Distributable)
                {
                    throw new InvalidOperationException($"{addon.Id} {release.Version} is not distributable; get it from {release.Website ?? "its website"}");
                }

                if (release.Zip is null || (release.Sha256 is null && release.Sha1 is null))
                {
                    throw new InvalidOperationException($"{addon.Id} {release.Version} manifest has no zip or no sha256/sha1; refusing to install");
                }

                folders = InstallFolders(addon, release);
                var zipUri = addon.ManifestBaseUrl is null
                    ? new Uri(release.Zip)
                    : new Uri(ManifestUri(addon, channel), release.Zip);
                try
                {
                    await DownloadAsync(_httpClient, zipUri, tempZipPath, release.Size, progress, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    ReportDownloadFailure(addon, release, zipUri, (int?)(ex as HttpRequestException)?.StatusCode, $"{ex.GetType().Name}: {ex.Message}");
                    throw;
                }

                try
                {
                    await VerifyChecksumAsync(tempZipPath, release.Sha256, release.Sha1, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    ReportDownloadFailure(addon, release, zipUri, null, "checksum mismatch");
                    throw;
                }

                if (addon.Source == CurseForgeAddons.Source)
                {
                    // CurseForge's module list can omit a folder its own zip ships (EllesmereUIForeverEssentials, 29 Sep 2026).
                    folders = [.. folders.Union(ZipFolders(tempZipPath, addon.Id), StringComparer.OrdinalIgnoreCase)];
                }

                RefuseForeignFolders(tempZipPath, folders);
                lock (AddOnsWriteLock)
                {
                    Uninstall(addOnsPath, folders);
                    ExtractZip(tempZipPath, addOnsPath);
                }
            }
            finally
            {
                if (File.Exists(tempZipPath))
                {
                    File.Delete(tempZipPath);
                }
            }

            _logger.Info($"Installed {addon.Id} {channel} {release.Version} into {addOnsPath}");
            return folders;
        }
        catch (Exception ex)
        {
            _logger.Err(ex, $"Failed to install {addon.Id} {channel} into {addOnsPath}");
            throw;
        }
    }

    private void ReportDownloadFailure(ManagedAddon addon, AddonRelease release, Uri zipUri, int? status, string error)
    {
        var parts = addon.Id.Split('-');
        if (_reportDownloadFailure is null
            || addon.Source != CurseForgeAddons.Source
            || parts is not ["curseforge", var mod, _]
            || !int.TryParse(mod, out var modId)
            || !_reportedFailures.TryAdd($"{addon.Id}|{release.Version}", 0))
        {
            return;
        }

        var failure = new CurseForgeDownloadFailure(modId, release.Version, zipUri.AbsoluteUri, status, error.Length > 500 ? error[..500] : error);
        _ = Task.Run(async () =>
        {
            try
            {
                await _reportDownloadFailure(failure, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Could not report the {addon.Id} {release.Version} download failure to Steward API");
            }
        });
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

    [SuppressMessage("Security", "CA5350", Justification = "CurseForge publishes only SHA-1 and MD5 file hashes; SHA-256 is used whenever the manifest has it")]
    internal static async Task VerifyChecksumAsync(
        string filePath,
        string? expectedSha256,
        string? expectedSha1,
        CancellationToken cancellationToken)
    {
        var (name, expected) = expectedSha256 is not null ? ("SHA-256", expectedSha256)
            : expectedSha1 is not null ? ("SHA-1", expectedSha1)
            : throw new InvalidOperationException("The manifest has neither sha256 nor sha1; refusing to install an unverified zip");

        string actualHash;
        await using (var stream = File.OpenRead(filePath))
        {
            actualHash = Convert.ToHexString(expectedSha256 is not null
                ? await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)
                : await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        }

        if (!string.Equals(actualHash, expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(filePath);
            throw new InvalidOperationException(
                $"{name} mismatch: expected {expected}, got {actualHash}");
        }
    }

    public static IReadOnlyList<string> InstallFolders(ManagedAddon addon, AddonRelease? release)
    {
        ArgumentNullException.ThrowIfNull(addon);
        var folders = release?.Folders is { Count: > 0 } listed ? listed
            : addon.Folders is { Count: > 0 } known ? known
            : [addon.FolderName];
        return ValidFolders(folders, addon.Id);
    }

    internal static IReadOnlyList<string> ZipFolders(string zipPath, string addonId)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        return ValidFolders(
            [.. archive.Entries.Select(entry => entry.FullName.Split('/', '\\')).Where(parts => parts.Length > 1).Select(parts => parts[0]).Distinct(StringComparer.OrdinalIgnoreCase)],
            addonId);
    }

    private static IReadOnlyList<string> ValidFolders(IReadOnlyList<string> folders, string addonId)
    {
        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder) || folder is "." or ".." || folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidOperationException($"{addonId} lists an invalid folder name '{folder}'");
            }
        }

        return folders;
    }

    public static void Uninstall(string addOnsPath, IReadOnlyList<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        if (folders.Select(folder => RemovalRefusal(addOnsPath, folder)).FirstOrDefault(message => message is not null) is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }

        foreach (var folder in folders)
        {
            RemoveExistingInstall(addOnsPath, folder);
        }
    }

    internal static void RefuseForeignFolders(string zipPath, IReadOnlyList<string> folders)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var parts = entry.FullName.Split('/', '\\');
            if (parts.Length < 2 || !folders.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Zip entry {entry.FullName} is outside the expected folders {string.Join(", ", folders)}");
            }
        }
    }

    public static void RemoveExistingInstall(string addOnsPath, string folderName)
    {
        var existingPath = Path.Combine(addOnsPath, folderName);
        if (!Directory.Exists(existingPath))
        {
            return;
        }

        if (RemovalRefusal(addOnsPath, folderName) is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }

        foreach (var file in Directory.EnumerateFiles(existingPath, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(existingPath, recursive: true);
    }

    public static string? RemovalRefusal(string addOnsPath, string folderName)
    {
        var existingPath = Path.Combine(addOnsPath, folderName);
        return !Directory.Exists(existingPath) || LocalAddons.TopLevelToc(existingPath, folderName) is not null
            ? null
            : $"{existingPath} exists but has no {folderName}.toc or {folderName}_<flavour>.toc; refusing to delete it";
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
