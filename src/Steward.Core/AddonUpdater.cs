using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using Steward.Core.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Steward.Core;

public sealed class ManifestCache
{
    private readonly ConcurrentDictionary<string, CachedManifest> _entries = new(StringComparer.Ordinal);

    public sealed record CachedManifest(string ETag, AddonRelease? Release);

    public CachedManifest? Get(Uri manifestUri) => _entries.GetValueOrDefault(manifestUri.AbsoluteUri);

    public void Set(Uri manifestUri, string? etag, AddonRelease? release)
    {
        if (string.IsNullOrEmpty(etag))
        {
            _entries.TryRemove(manifestUri.AbsoluteUri, out _);
        }
        else
        {
            _entries[manifestUri.AbsoluteUri] = new CachedManifest(etag, release);
        }
    }

    public void Clear() => _entries.Clear();
}

public sealed class AddonUpdater
{
    internal static readonly Lock AddOnsWriteLock = new();

    // The Windows client reads AddOns (natively or under Wine), so a folder name follows Windows' file name rules on every OS.
    private static readonly char[] InvalidFolderChars = [.. "\"<>|:*?\\/", .. Enumerable.Range(0, 32).Select(code => (char)code)];

    private readonly HttpClient _httpClient;
    private readonly HttpClient? _sessionClient;
    private readonly ILogger _logger;
    private readonly Func<CurseForgeDownloadFailure, CancellationToken, Task>? _reportDownloadFailure;
    private readonly ConcurrentDictionary<string, byte> _reportedFailures = new();
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<long, byte> _badDirectFiles = new();
    private long _directSkipUntilTicks;

    public AddonUpdater(
        HttpClient httpClient,
        ILogger<AddonUpdater>? logger = null,
        HttpClient? sessionClient = null,
        Func<CurseForgeDownloadFailure, CancellationToken, Task>? reportDownloadFailure = null,
        TimeProvider? clock = null)
    {
        _httpClient = httpClient;
        _sessionClient = sessionClient;
        _reportDownloadFailure = reportDownloadFailure;
        _logger = logger ?? NullLogger<AddonUpdater>.Instance;
        _clock = clock ?? TimeProvider.System;
        TukUi = new TukUiSource(httpClient, _clock, _logger);
    }

    public TukUiSource TukUi { get; }

    internal static readonly TimeSpan DirectTimeout = TimeSpan.FromSeconds(10);

    internal static readonly TimeSpan DirectSkip = TimeSpan.FromHours(1);

    public ManifestCache ManifestCache { get; } = new();

    public async Task<AddonRelease?> GetLatestAsync(ManagedAddon addon, string channel, CancellationToken cancellationToken, bool noCache = false, bool revalidate = true)
    {
        ArgumentNullException.ThrowIfNull(addon);
        if (TukUiAddons.ProjectId(addon) is { } projectId)
        {
            return channel == "release" ? await TukUi.ReleaseAsync(projectId, noCache, cancellationToken).ConfigureAwait(false) : null;
        }

        var client = addon.ManifestNeedsSession ? _sessionClient ?? _httpClient : _httpClient;
        return await FetchManifestAsync(client, ManifestUri(addon, channel), cancellationToken, noCache, revalidate ? ManifestCache : null).ConfigureAwait(false);
    }

    public Task<byte[]> GetIconAsync(Uri iconUri, CancellationToken cancellationToken) =>
        _httpClient.GetByteArrayAsync(iconUri, cancellationToken);

    public static async Task<AddonRelease?> FetchManifestAsync(
        HttpClient httpClient, Uri manifestUri, CancellationToken cancellationToken, bool noCache = false, ManifestCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(manifestUri);

        using var request = new HttpRequestMessage(HttpMethod.Get, manifestUri);
        if (noCache)
        {
            // steward-server serves a CurseForge manifest up to 15 minutes old, or 1 minute old on no-cache.
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        }

        var cached = cache?.Get(manifestUri);
        if (cached is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", cached.ETag);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return cached is not null
                ? cached.Release
                : await FetchManifestAsync(httpClient, manifestUri, cancellationToken, noCache).ConfigureAwait(false);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SessionExpiredException();
        }

        response.EnsureSuccessStatusCode();

        // A channel with no releases publishes the literal JSON `null`, so null here means an empty channel rather than a fault.
        var release = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.AddonRelease, cancellationToken)
            .ConfigureAwait(false);
        cache?.Set(manifestUri, response.Headers.ETag?.ToString(), release);
        return release;
    }

    public async Task<IReadOnlyDictionary<string, AddonRelease?>> ProbeChannelsAsync(
        ManagedAddon addon, IReadOnlyList<string> channels, CancellationToken cancellationToken, bool noCache = false, bool direct = false)
    {
        ArgumentNullException.ThrowIfNull(addon);
        ArgumentNullException.ThrowIfNull(channels);
        var listed = direct ? await ProbeDirectAsync(addon, cancellationToken).ConfigureAwait(false) : null;
        var releases = await Task.WhenAll(channels.Select(channel => DirectRelease(listed, channel) is var (useDirect, found) && useDirect
                ? Task.FromResult(found)
                : ProbeChannelAsync(addon, channel, noCache, cancellationToken)))
            .ConfigureAwait(false);

        var result = new Dictionary<string, AddonRelease?>(channels.Count);
        for (var i = 0; i < channels.Count; i++)
        {
            result[channels[i]] = releases[i];
        }

        return result;
    }

    private sealed record DirectFiles(CurseForgePublicFiles Files, int VersionType, string? Website);

    private (bool UseDirect, AddonRelease? Release) DirectRelease(DirectFiles? listed, string channel)
    {
        if (listed is null)
        {
            return (false, null);
        }

        var file = CurseForgeDirect.Select(listed.Files.Items, channel, listed.VersionType);
        return file is null ? (listed.Files.Complete && !CurseForgeDirect.HasUnavailable(listed.Files.Items, channel, listed.VersionType), null)
            : _badDirectFiles.ContainsKey(file.Id) ? (false, null)
            : (true, CurseForgeDirect.ToRelease(file, listed.Website));
    }

    private bool DirectSkipped => _clock.GetUtcNow().UtcTicks < Volatile.Read(ref _directSkipUntilTicks);

    private async Task<DirectFiles?> ProbeDirectAsync(ManagedAddon addon, CancellationToken cancellationToken)
    {
        if (!addon.IsCurseForge || CurseForgeDirect.Ids(addon.ManifestBaseUrl) is not (int modId, int versionType) || DirectSkipped)
        {
            return null;
        }

        try
        {
            var files = await GetDirectAsync(CurseForgeDirect.FilesUri(modId), CurseForgeDirect.ParseFiles, cancellationToken).ConfigureAwait(false);
            if (files.Items.Count == 0)
            {
                _logger.Info($"CurseForge direct for {addon.Id} listed no usable files; using Steward API for it");
                return null;
            }

            return new DirectFiles(files, versionType, addon.Website);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            DirectFailed(addon, ex);
            return null;
        }
    }

    public async Task<AddonRelease> WithDirectRequiresAsync(ManagedAddon addon, string channel, AddonRelease release, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(addon);
        ArgumentNullException.ThrowIfNull(release);
        if (!release.Direct || release.Requires is not null || CurseForgeDirect.Ids(addon.ManifestBaseUrl) is not (int modId, _))
        {
            return release;
        }

        if (DirectSkipped)
        {
            return await ProxyReleaseAsync(addon, channel, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return release with { Requires = await GetDirectAsync(CurseForgeDirect.DependenciesUri(modId), CurseForgeDirect.ParseRequires, cancellationToken).ConfigureAwait(false) };
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            _logger.Info($"CurseForge direct dependencies for {addon.Id} failed ({FailureReason(ex)}); installing from Steward API");
            return await ProxyReleaseAsync(addon, channel, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<AddonRelease> ProxyReleaseAsync(ManagedAddon addon, string channel, CancellationToken cancellationToken) =>
        await GetLatestAsync(addon, channel, cancellationToken, revalidate: false).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"{addon.Id} has no {channel} build on Steward API");

    private async Task<T> GetDirectAsync<T>(Uri uri, Func<JsonElement, T> parse, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DirectTimeout);
        using var response = await _httpClient.GetAsync(uri, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        return parse(document.RootElement);
    }

    private static bool IsNetworkLevel(Exception ex) =>
        ex is OperationCanceledException or JsonException
        || (ex is HttpRequestException http && (http.StatusCode is null or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests || (int)http.StatusCode >= 500));

    private static string FailureReason(Exception ex) =>
        ex is HttpRequestException { StatusCode: { } status } ? $"status {(int)status}" : $"{ex.GetType().Name}: {ex.Message}";

    private void DirectFailed(ManagedAddon addon, Exception ex)
    {
        if (!IsNetworkLevel(ex))
        {
            _logger.Info($"CurseForge direct for {addon.Id} failed ({FailureReason(ex)}); using Steward API for it");
            return;
        }

        Interlocked.Exchange(ref _directSkipUntilTicks, (_clock.GetUtcNow() + DirectSkip).UtcTicks);
        _logger.Info($"CurseForge direct for {addon.Id} failed ({FailureReason(ex)}); using Steward API for every addon for {DirectSkip.TotalMinutes:0} minutes");
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

    public async Task<(IReadOnlyList<string> Folders, AddonRelease Release)> InstallRefetchingOn404Async(
        ManagedAddon addon,
        string channel,
        AddonRelease release,
        string addOnsPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? foreignFolders = null)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (release.Direct && DirectSkipped)
        {
            return await InstallRefetchingOn404Async(addon, channel, await ProxyReleaseAsync(addon, channel, cancellationToken).ConfigureAwait(false), addOnsPath, progress, cancellationToken, foreignFolders).ConfigureAwait(false);
        }

        try
        {
            return (await InstallAsync(addon, channel, release, addOnsPath, progress, cancellationToken, foreignFolders).ConfigureAwait(false), release);
        }
        catch (Exception ex) when (release.Direct && !cancellationToken.IsCancellationRequested)
        {
            DirectFailed(addon, ex);
            var proxied = await GetLatestAsync(addon, channel, cancellationToken, revalidate: false).ConfigureAwait(false);
            if (proxied is null)
            {
                throw;
            }

            var installed = await InstallRefetchingOn404Async(addon, channel, proxied, addOnsPath, progress, cancellationToken, foreignFolders).ConfigureAwait(false);
            if (!IsNetworkLevel(ex) || !string.Equals(installed.Release.Version, release.Version, StringComparison.Ordinal))
            {
                _badDirectFiles.TryAdd(release.DirectFileId, 0);
            }

            return installed;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // A manifest fetched before a publish can name a zip steward-server has since deleted.
            var fresh = await GetLatestAsync(addon, channel, cancellationToken, revalidate: false).ConfigureAwait(false);
            if (fresh is null)
            {
                throw;
            }

            _logger.Info($"Zip for {addon.Id} {release.Version} returned 404; retrying with {fresh.Version}");
            return (await InstallAsync(addon, channel, fresh, addOnsPath, progress, cancellationToken, foreignFolders).ConfigureAwait(false), fresh);
        }
    }

    public async Task<IReadOnlyList<string>> InstallAsync(
        ManagedAddon addon,
        string channel,
        AddonRelease release,
        string addOnsPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? foreignFolders = null)
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

                if (release.Zip is null || (!addon.IsCurseForge && release.TukUiProjectId is null && release.Sha256 is null))
                {
                    throw new InvalidOperationException($"{addon.Id} {release.Version} manifest has no zip or no sha256; refusing to install");
                }

                folders = InstallFolders(addon, release);
                var zipUri = release.TukUiProjectId is { } projectId
                    ? await TukUi.ZipUriAsync(projectId, release.Version, cancellationToken).ConfigureAwait(false)
                    : ZipUri(addon, channel, release.Zip);
                try
                {
                    await DownloadAsync(_httpClient, zipUri, tempZipPath, release.Size, progress, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    ReportDownloadFailure(addon, release, zipUri, (int?)(ex as HttpRequestException)?.StatusCode, $"{ex.GetType().Name}: {ex.Message}");
                    throw;
                }

                if (addon.IsCurseForge)
                {
                    try
                    {
                        VerifyZip(tempZipPath, release.Size);
                    }
                    catch (Exception ex) when (!release.Direct && ex is InvalidOperationException or InvalidDataException)
                    {
                        ReportDownloadFailure(addon, release, zipUri, null, ex.Message);
                        throw;
                    }
                }
                else if (release.TukUiProjectId is null)
                {
                    await VerifyChecksumAsync(tempZipPath, release.Sha256, cancellationToken).ConfigureAwait(false);
                }

                if (addon.IsCurseForge)
                {
                    // CurseForge's module list can omit a folder its own zip ships (EllesmereUIForeverEssentials, 29 Sep 2026).
                    folders = release.Direct ? ZipTocFolders(tempZipPath, addon.Id) : [.. folders.Union(ZipFolders(tempZipPath, addon.Id), StringComparer.OrdinalIgnoreCase)];
                    if (foreignFolders is not null && folders.FirstOrDefault(folder => foreignFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) is { } taken)
                    {
                        throw new InvalidOperationException($"{addon.Id} ships {taken}, which belongs to another addon; refusing to install");
                    }
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
            || !addon.IsCurseForge
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

    internal static Uri ZipUri(ManagedAddon addon, string channel, string zip) =>
        Uri.TryCreate(zip, UriKind.Absolute, out var absolute) ? absolute
        : addon.ZipResolvesAgainstManifest && addon.ManifestBaseUrl is not null ? new Uri(ManifestUri(addon, channel), zip)
        : throw new InvalidOperationException($"{addon.Id} manifest zip '{zip}' is not an absolute URL");

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
        expectedSize = expectedSize > 0 ? expectedSize : response.Content.Headers.ContentLength ?? 0;

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

    internal static async Task VerifyChecksumAsync(string filePath, string? expectedSha256, CancellationToken cancellationToken)
    {
        if (expectedSha256 is null)
        {
            throw new InvalidOperationException("The manifest has no sha256; refusing to install an unverified zip");
        }

        string actualHash;
        await using (var stream = File.OpenRead(filePath))
        {
            actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        }

        if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(filePath);
            throw new InvalidOperationException($"SHA-256 mismatch: expected {expectedSha256}, got {actualHash}");
        }
    }

    internal static void VerifyZip(string filePath, long expectedSize)
    {
        var actualSize = new FileInfo(filePath).Length;
        if (actualSize != expectedSize)
        {
            throw new InvalidOperationException($"Size mismatch: expected {expectedSize} bytes, got {actualSize}");
        }

        using var archive = ZipFile.OpenRead(filePath);
        foreach (var entry in archive.Entries)
        {
            // ZipArchiveEntry.Open does not check the entry's CRC on read (.NET 10, verified 23 Oct 2026).
            using var stream = entry.Open();
            if (Crc32(stream) != entry.Crc32)
            {
                throw new InvalidDataException($"Zip entry {entry.FullName} fails its CRC check");
            }
        }
    }

    private static readonly uint[] CrcTable = [.. Enumerable.Range(0, 256).Select(n =>
    {
        var crc = (uint)n;
        for (var bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }

        return crc;
    })];

    private static uint Crc32(Stream stream)
    {
        var crc = 0xFFFFFFFFu;
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            foreach (var value in buffer.AsSpan(0, read))
            {
                crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
            }
        }

        return ~crc;
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

    internal static IReadOnlyList<string> ZipTocFolders(string zipPath, string addonId)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        IReadOnlyList<string> folders = [.. archive.Entries.Select(entry => entry.FullName.Split('/', '\\'))
            .Where(parts => parts.Length == 2 && parts[1].EndsWith(".toc", StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        return folders.Count > 0 ? ValidFolders(folders, addonId) : throw new InvalidOperationException($"{addonId} zip has no folder with a .toc");
    }

    private static IReadOnlyList<string> ValidFolders(IReadOnlyList<string> folders, string addonId)
    {
        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder) || folder is "." or ".." || folder.IndexOfAny(InvalidFolderChars) >= 0)
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
            var parts = EntryParts(entry.FullName);
            if (parts.Length < 2 || !folders.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Zip entry {entry.FullName} is outside the expected folders {string.Join(", ", folders)}");
            }
        }
    }

    private static string[] EntryParts(string fullName)
    {
        var parts = fullName.Split('/', '\\');
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i] is "." or ".." || (parts[i].Length == 0 && i < parts.Length - 1) || parts[i].Contains(':', StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Zip entry {fullName} has an unsafe path");
            }
        }

        return parts;
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
            EntryParts(fileEntries[i].FullName);
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
