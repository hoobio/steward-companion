using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Steward.Core;

namespace Steward.App.Services;

public enum GuideSyncOutcome
{
    UpToDate,
    Stale,
    Downloaded,
    Written,
    NeedsNewerAddon,
    Rejected,
}

public sealed record GuideSyncResult(GuideSyncOutcome Outcome, DateTimeOffset UpdatedAt, string? Error = null);

public sealed class RestedXpService : IDisposable
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan KeepAliveMargin = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(10);

    private const string RejectedMessage = "The addon rejected the string";

    private readonly RestedXpClient _client;
    private readonly AppStateStore _stateStore;
    private readonly IReadOnlyDictionary<string, string[]> _productPrefixes;
    private readonly string _cacheFolder;
    private readonly Dictionary<string, DateTimeOffset> _lastFailure = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    private string? _pendingPassword;

    public RestedXpService(
        RestedXpClient client,
        AppStateStore stateStore,
        IReadOnlyDictionary<string, string[]> productPrefixes)
    {
        _client = client;
        _stateStore = stateStore;
        _productPrefixes = productPrefixes;
        _cacheFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steward", "guides");
    }

    public void Dispose() => _syncGate.Dispose();

    public RestedXpSession? Session { get; private set; }

    public IReadOnlyList<string> Products { get; private set; } = [];

    public IReadOnlyDictionary<string, long> Timestamps { get; private set; } = new Dictionary<string, long>();

    public bool IsSignedIn => Session is not null;

    public string? Username => Session?.Username;

    public string? BattleTag { get; private set; }

    public bool TryRestore()
    {
        var stored = _stateStore.Load().EncryptedRestedXpSession;
        if (stored is null)
        {
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(
                Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser));
            Session = JsonSerializer.Deserialize(json, CompanionJsonContext.Default.RestedXpSession);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException)
        {
            Session = null;
        }

        if (Session is not null)
        {
            RestoreBattleTag();
        }

        return Session is not null;
    }

    private void RestoreBattleTag()
    {
        try
        {
            if (new DirectoryInfo(_cacheFolder).EnumerateFiles("*.json").MaxBy(file => file.LastWriteTimeUtc) is not { } newest)
            {
                return;
            }

            BattleTag = JsonSerializer
                .Deserialize(File.ReadAllText(newest.FullName), CompanionJsonContext.Default.RestedXpCachedGuide)?.BnetTag;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
    }

    public async Task<bool> SignInAsync(string username, string password, CancellationToken cancellationToken)
    {
        try
        {
            Store(username, await _client.SignInAsync(username, password, null, cancellationToken).ConfigureAwait(true));
        }
        catch (RestedXpMfaRequiredException)
        {
            _pendingPassword = password;
            return true;
        }

        return false;
    }

    public async Task VerifyMfaAsync(string username, string code, CancellationToken cancellationToken)
    {
        if (_pendingPassword is not { } password)
        {
            throw new RestedXpSignInException("Sign in again for a new code.");
        }

        try
        {
            Store(username, await _client.SignInAsync(username, password, code, cancellationToken).ConfigureAwait(true));
        }
        catch (RestedXpMfaRequiredException)
        {
            throw new RestedXpSignInException("That code was not accepted");
        }
    }

    public void ForgetPendingPassword() => _pendingPassword = null;

    public void SignOut()
    {
        Session = null;
        _pendingPassword = null;
        BattleTag = null;
        Products = [];
        _lastFailure.Clear();
        _stateStore.Save(_stateStore.Load() with { EncryptedRestedXpSession = null });
    }

    public async Task EnsureFreshSessionAsync(bool forCall, CancellationToken cancellationToken)
    {
        if (Session is not { } session)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (session.RefreshExpiresAt is { } expiresAt && expiresAt <= now)
        {
            SignOut();
            throw new RestedXpSessionExpiredException();
        }

        // ponytail: an offline refresh token has no expiry, so only a bounded one is renewed near its end
        var keepAlive = session.RefreshExpiresAt is { } deadline && deadline - now <= KeepAliveMargin;
        var accessStale = forCall && session.AccessExpiresAt - now <= RefreshMargin;
        if (!keepAlive && !accessStale)
        {
            return;
        }

        try
        {
            Store(session.Username, await _client.RefreshAsync(session.RefreshToken, cancellationToken).ConfigureAwait(true));
        }
        catch (RestedXpSessionExpiredException)
        {
            SignOut();
            throw;
        }
    }

    public async Task LoadCatalogueAsync(CancellationToken cancellationToken)
    {
        if (Session is not { } session)
        {
            return;
        }

        try
        {
            Products = [.. (await _client.GetProductsAsync(session, cancellationToken).ConfigureAwait(true)).Select(p => p.ProductName)];
        }
        catch (RestedXpSessionExpiredException)
        {
            SignOut();
            throw;
        }

        Timestamps = await _client.GetTimestampsAsync(cancellationToken).ConfigureAwait(true);
    }

    public bool IsAllowed(WowInstall install, string productName)
    {
        ArgumentNullException.ThrowIfNull(install);

        return install.ProductCode is { } code
            && _productPrefixes.TryGetValue(code, out var prefixes)
            && prefixes.Any(prefix => productName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<string> GuideChoices(WowInstall install)
    {
        ArgumentNullException.ThrowIfNull(install);

        return _stateStore.Load().RestedXpGuideChoices.GetValueOrDefault(install.FlavourPath) is { } stored
            ? [.. stored.Where(product => Products.Contains(product, StringComparer.Ordinal))]
            : [.. Products.Where(product => IsAllowed(install, product))];
    }

    public void SetGuideChoices(string flavourPath, IReadOnlyList<string> products)
    {
        var state = _stateStore.Load();
        state.RestedXpGuideChoices[flavourPath] = [.. products];
        _stateStore.Save(state);
    }

    public async Task<IReadOnlyDictionary<string, GuideSyncResult>> SyncAsync(
        WowInstall install, IReadOnlyList<string> products, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(products);

        await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            return await SyncCoreAsync(install, products, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task<IReadOnlyDictionary<string, GuideSyncResult>> SyncCoreAsync(
        WowInstall install, IReadOnlyList<string> products, CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, GuideSyncResult>(StringComparer.Ordinal);
        var strings = new List<(string Name, string Text)>();
        var serverTimestamps = new Dictionary<string, long>(StringComparer.Ordinal);

        var state = _stateStore.Load();
        var prefix = AppStateStore.Key(install.FlavourPath, string.Empty);
        var recorded = state.RestedXpGuides
            .Where(entry => entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => entry.Key[prefix.Length..], entry => entry.Value.Timestamp, StringComparer.Ordinal);
        var changed = !recorded.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(products);

        foreach (var productName in products)
        {
            if (!Timestamps.TryGetValue(productName, out var serverTimestamp))
            {
                results[productName] = new GuideSyncResult(
                    GuideSyncOutcome.Stale, DateTimeOffset.UnixEpoch, $"RestedXP publish no timestamp for {productName}.");
                continue;
            }

            var updatedAt = DateTimeOffset.FromUnixTimeMilliseconds(serverTimestamp);
            var guide = ReadCached(productName, serverTimestamp);
            if (guide is null)
            {
                var (downloaded, error) = await FetchAsync(productName, serverTimestamp, cancellationToken).ConfigureAwait(true);
                if (downloaded is null)
                {
                    results[productName] = new GuideSyncResult(GuideSyncOutcome.Stale, updatedAt, error);
                    continue;
                }

                guide = downloaded;
            }

            changed |= recorded.GetValueOrDefault(productName) != serverTimestamp;
            serverTimestamps[productName] = serverTimestamp;
            strings.Add((productName, guide));
            results[productName] = new GuideSyncResult(GuideSyncOutcome.UpToDate, updatedAt);
        }

        if (!changed || strings.Count != products.Count)
        {
            return results;
        }

        var writtenAt = DateTimeOffset.Now;
        var generation = writtenAt.ToUnixTimeMilliseconds();
        try
        {
            StewardGuidesAddon.Write(install.AddOnsPath, strings, generation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            foreach (var (productName, _) in strings)
            {
                results[productName] = new GuideSyncResult(
                    GuideSyncOutcome.Downloaded, DateTimeOffset.FromUnixTimeMilliseconds(serverTimestamps[productName]), ex.Message);
            }

            return results;
        }

        foreach (var staleKey in state.RestedXpGuides.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            state.RestedXpGuides.Remove(staleKey);
        }

        state.RestedXpGuidesGeneration[install.FlavourPath] = generation;
        foreach (var (productName, _) in strings)
        {
            var serverTimestamp = serverTimestamps[productName];
            state.RestedXpGuides[AppStateStore.Key(install.FlavourPath, productName)] =
                new RestedXpGuideRecord(serverTimestamp, writtenAt);
            results[productName] = new GuideSyncResult(
                GuideSyncOutcome.Written, DateTimeOffset.FromUnixTimeMilliseconds(serverTimestamp));
        }

        _stateStore.Save(state);
        return results;
    }

    public IReadOnlyDictionary<string, GuideSyncResult> Confirm(WowInstall install, IReadOnlyList<string> products)
    {
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(products);

        var results = new Dictionary<string, GuideSyncResult>(StringComparer.Ordinal);
        var state = _stateStore.Load();
        if (!state.RestedXpGuidesGeneration.TryGetValue(install.FlavourPath, out var generation))
        {
            return results;
        }

        var current = StewardGuidesSavedVariables.Read(install.FlavourPath)
            .Where(file => file.Generation == generation)
            .ToList();
        var rejected = new List<string>();
        foreach (var productName in products)
        {
            var key = AppStateStore.Key(install.FlavourPath, productName);
            if (!state.RestedXpGuides.TryGetValue(key, out var record)
                || ReadCached(productName, record.Timestamp) is not { } guide
                || StewardGuidesAddon.Hash(guide) is not { } hash)
            {
                continue;
            }

            var updatedAt = DateTimeOffset.FromUnixTimeMilliseconds(record.Timestamp);
            if (current.Any(file => file.Imported.Contains(hash)))
            {
                results[productName] = new GuideSyncResult(GuideSyncOutcome.UpToDate, updatedAt);
            }
            else if (current.Count > 0)
            {
                results[productName] = new GuideSyncResult(GuideSyncOutcome.Rejected, updatedAt, RejectedMessage);
                rejected.Add(key);
            }
            else
            {
                results[productName] = new GuideSyncResult(GuideSyncOutcome.Written, updatedAt);
            }
        }

        if (rejected.Count > 0)
        {
            foreach (var key in rejected)
            {
                state.RestedXpGuides.Remove(key);
            }

            _stateStore.Save(state);
        }

        return results;
    }

    private async Task<(string? Guide, string? Error)> FetchAsync(string productName, long timestamp, CancellationToken cancellationToken)
    {
        if (Session is not { } session)
        {
            return (null, null);
        }

        if (_lastFailure.TryGetValue(productName, out var failedAt) && DateTimeOffset.UtcNow - failedAt < RetryAfterFailure)
        {
            return (null, null);
        }

        try
        {
            await EnsureFreshSessionAsync(forCall: true, cancellationToken).ConfigureAwait(true);
            session = Session ?? session;
            var downloaded = await _client.DownloadGuideAsync(session, productName, cancellationToken).ConfigureAwait(true);
            Cache(productName, timestamp, downloaded);
            return (downloaded.Guide, null);
        }
        catch (RestedXpSessionExpiredException)
        {
            SignOut();
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or TaskCanceledException)
        {
            _lastFailure[productName] = DateTimeOffset.UtcNow;
            return (null, ex.Message);
        }
    }

    private void Store(string username, RestedXpTokens tokens)
    {
        Session = new RestedXpSession(
            username, tokens.AccessToken, tokens.RefreshToken, tokens.AccessExpiresAt, tokens.RefreshExpiresAt);
        _pendingPassword = null;

        var json = JsonSerializer.Serialize(Session, CompanionJsonContext.Default.RestedXpSession);
        var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
        _stateStore.Save(_stateStore.Load() with { EncryptedRestedXpSession = Convert.ToBase64String(blob) });
    }

    private string CachePath(string productName, string extension)
    {
        var name = string.Join('_', productName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_cacheFolder, name + extension);
    }

    private string? ReadCached(string productName, long timestamp)
    {
        try
        {
            var metaPath = CachePath(productName, ".json");
            var guidePath = CachePath(productName, ".txt");
            if (!File.Exists(metaPath) || !File.Exists(guidePath))
            {
                return null;
            }

            var meta = JsonSerializer.Deserialize(File.ReadAllText(metaPath), CompanionJsonContext.Default.RestedXpCachedGuide);
            return meta?.Timestamp == timestamp ? File.ReadAllText(guidePath) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Cache(string productName, long timestamp, RestedXpGuide guide)
    {
        Directory.CreateDirectory(_cacheFolder);
        BattleTag = guide.BnetTag ?? BattleTag;
        File.WriteAllText(CachePath(productName, ".txt"), guide.Guide);
        File.WriteAllText(
            CachePath(productName, ".json"),
            JsonSerializer.Serialize(new RestedXpCachedGuide(timestamp, guide.BnetTag), CompanionJsonContext.Default.RestedXpCachedGuide));
    }
}
