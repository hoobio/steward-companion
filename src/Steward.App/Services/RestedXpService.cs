using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Steward.Core;

using Microsoft.Extensions.Logging;

namespace Steward.App.Services;

public enum GuideSyncOutcome
{
    UpToDate,
    Stale,
    Downloaded,
    Written,
    NoAccountFiles,
}

public sealed record GuideSyncResult(GuideSyncOutcome Outcome, DateTimeOffset UpdatedAt, string? Error = null);

public sealed partial class RestedXpService
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(10);

    private readonly RestedXpClient _client;
    private readonly AppStateStore _stateStore;
    private readonly ILogger<RestedXpService> _logger;
    private readonly string _cacheFolder;
    private readonly Dictionary<string, DateTimeOffset> _lastFailure = new(StringComparer.OrdinalIgnoreCase);

    private string? _mfaSessionId;

    public RestedXpService(RestedXpClient client, AppStateStore stateStore, ILogger<RestedXpService> logger)
    {
        _client = client;
        _stateStore = stateStore;
        _logger = logger;
        _cacheFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steward", "guides");
    }

    public RestedXpSession? Session { get; private set; }

    public IReadOnlyList<string> Products { get; private set; } = [];

    public IReadOnlyDictionary<string, long> Timestamps { get; private set; } = new Dictionary<string, long>();

    public bool IsSignedIn => Session is not null;

    public string? Username => Session?.Username;

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

        return Session is not null;
    }

    public async Task<bool> SignInAsync(string username, string password, CancellationToken cancellationToken)
    {
        var response = await _client.SignInAsync(username, password, cancellationToken).ConfigureAwait(true);
        if (response.MfaRequired)
        {
            _mfaSessionId = response.SessionId;
            return true;
        }

        Store(username, response);
        return false;
    }

    public async Task VerifyMfaAsync(string username, string code, CancellationToken cancellationToken)
    {
        if (_mfaSessionId is null)
        {
            throw new RestedXpSignInException("Sign in again to get a new code prompt.");
        }

        try
        {
            Store(username, await _client.VerifyMfaAsync(_mfaSessionId, code, cancellationToken).ConfigureAwait(true));
        }
        catch (RestedXpSignInException ex) when (ex.ResponseKeys.Count > 0)
        {
            LogUnexpectedMfaShape(string.Join(", ", ex.ResponseKeys));
            throw;
        }
    }

    public void SignOut()
    {
        Session = null;
        _mfaSessionId = null;
        Products = [];
        _lastFailure.Clear();
        _stateStore.Save(_stateStore.Load() with { EncryptedRestedXpSession = null });
    }

    public async Task EnsureFreshSessionAsync(CancellationToken cancellationToken)
    {
        if (Session is not { } session)
        {
            return;
        }

        if (session.RefreshExpiresAt <= DateTimeOffset.UtcNow)
        {
            SignOut();
            throw new RestedXpSessionExpiredException();
        }

        if (session.AccessExpiresAt - DateTimeOffset.UtcNow > RefreshMargin)
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

    public string? DefaultProduct => Products.FirstOrDefault(name => name.StartsWith("Forever", StringComparison.OrdinalIgnoreCase))
        ?? (Products.Count > 0 ? Products[0] : null);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RestedXP verify-mfa answered an unrecognised shape with keys {Keys}")]
    private partial void LogUnexpectedMfaShape(string keys);

    public string? GuideChoice(string flavourPath) => _stateStore.Load().RestedXpGuideChoice.GetValueOrDefault(flavourPath);

    public void SetGuideChoice(string flavourPath, string productName)
    {
        var state = _stateStore.Load();
        state.RestedXpGuideChoice[flavourPath] = productName;
        _stateStore.Save(state);
    }

    public async Task<GuideSyncResult> SyncAsync(WowInstall install, string productName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(install);

        if (!Timestamps.TryGetValue(productName, out var serverTimestamp))
        {
            return new GuideSyncResult(GuideSyncOutcome.Stale, DateTimeOffset.UnixEpoch, $"RestedXP publish no timestamp for {productName}.");
        }

        var updatedAt = DateTimeOffset.FromUnixTimeMilliseconds(serverTimestamp);
        var key = AppStateStore.Key(install.FlavourPath, productName);
        if (_stateStore.Load().RestedXpGuides.GetValueOrDefault(key)?.Timestamp == serverTimestamp)
        {
            return new GuideSyncResult(GuideSyncOutcome.UpToDate, updatedAt);
        }

        var guide = ReadCached(productName, serverTimestamp);
        if (guide is null)
        {
            if (Session is not { } session)
            {
                return new GuideSyncResult(GuideSyncOutcome.Stale, updatedAt);
            }

            if (_lastFailure.TryGetValue(productName, out var failedAt) && DateTimeOffset.UtcNow - failedAt < RetryAfterFailure)
            {
                return new GuideSyncResult(GuideSyncOutcome.Stale, updatedAt);
            }

            try
            {
                var downloaded = await _client.DownloadGuideAsync(session, productName, cancellationToken).ConfigureAwait(true);
                guide = downloaded.Guide;
                Cache(productName, serverTimestamp, downloaded);
            }
            catch (RestedXpSessionExpiredException)
            {
                SignOut();
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or TaskCanceledException)
            {
                _lastFailure[productName] = DateTimeOffset.UtcNow;
                return new GuideSyncResult(GuideSyncOutcome.Stale, updatedAt, ex.Message);
            }
        }

        // WoW rewrites SavedVariables from memory on logout, so a write made while the client runs is discarded.
        if (WowClient.IsRunning(install))
        {
            return new GuideSyncResult(GuideSyncOutcome.Downloaded, updatedAt);
        }

        int written;
        try
        {
            written = RxpGuideString.Write(install.FlavourPath, guide);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new GuideSyncResult(GuideSyncOutcome.Downloaded, updatedAt, ex.Message);
        }

        if (written == 0)
        {
            return new GuideSyncResult(GuideSyncOutcome.NoAccountFiles, updatedAt);
        }

        var state = _stateStore.Load();
        state.RestedXpGuides[key] = new RestedXpGuideRecord(serverTimestamp, DateTimeOffset.Now);
        _stateStore.Save(state);
        return new GuideSyncResult(GuideSyncOutcome.Written, updatedAt);
    }

    private void Store(string username, RestedXpLoginResponse response)
    {
        if (response.AccessToken is null || response.RefreshToken is null)
        {
            throw new RestedXpSignInException("Sign-in returned an unexpected response");
        }

        var now = DateTimeOffset.UtcNow;
        Session = new RestedXpSession(
            username,
            response.AccessToken,
            response.RefreshToken,
            now.AddSeconds(response.ExpiresIn),
            now.AddSeconds(response.RefreshExpiresIn));
        _mfaSessionId = null;

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
        File.WriteAllText(CachePath(productName, ".txt"), guide.Guide);
        File.WriteAllText(
            CachePath(productName, ".json"),
            JsonSerializer.Serialize(new RestedXpCachedGuide(timestamp, guide.BnetTag), CompanionJsonContext.Default.RestedXpCachedGuide));
    }
}
