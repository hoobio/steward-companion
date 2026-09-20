using System.Text.Json.Serialization;

namespace Steward.Core;

public sealed record ManagedAddon(string Id, string FolderName, string ManifestBaseUrl, bool AutoInstall = false, string? GitHubRepo = null);

public sealed record AddonRelease(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("zip")] string Zip,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("released")] DateTimeOffset Released);

public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt,
    [property: JsonPropertyName("assets")] GitHubAsset[] Assets);

public sealed record GitHubAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("digest")] string? Digest,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);

public sealed record AdminUser(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl,
    [property: JsonPropertyName("role")] string? Role);

public sealed record AdminMe(
    [property: JsonPropertyName("user")] AdminUser User);

public sealed record DesktopExchangeRequest(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("verifier")] string Verifier);

public sealed record DesktopToken(
    [property: JsonPropertyName("token")] string Token);

public sealed record WowInstall(
    string Root,
    string Flavour,
    string FlavourPath,
    string AddOnsPath,
    string? ProductCode,
    string? ClientVersion,
    string DisplayName);

public sealed record InstalledAddonRecord(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("sha256")] string Sha256);

public sealed record AppState(
    [property: JsonPropertyName("channels")] Dictionary<string, string> Channels,
    [property: JsonPropertyName("installs")] Dictionary<string, InstalledAddonRecord> Installs,
    [property: JsonPropertyName("session_token")] string? EncryptedSessionToken = null,
    [property: JsonPropertyName("added_installs")] List<string> AddedInstalls = null!,
    [property: JsonPropertyName("keep_in_tray")] bool KeepInTray = true)
{
    [JsonPropertyName("channel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyChannel { get; init; }
}

[JsonSerializable(typeof(AddonRelease))]
[JsonSerializable(typeof(AdminMe))]
[JsonSerializable(typeof(AdminUser))]
[JsonSerializable(typeof(AppState))]
[JsonSerializable(typeof(DesktopExchangeRequest))]
[JsonSerializable(typeof(DesktopToken))]
[JsonSerializable(typeof(GitHubRelease))]
public sealed partial class CompanionJsonContext : JsonSerializerContext;
