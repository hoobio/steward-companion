using System.Text.Json.Serialization;

namespace Steward.Core;

public sealed record ManagedAddon(string Id, string FolderName, string ManifestBaseUrl);

public sealed record AddonRelease(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("zip")] string Zip,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("released")] DateTimeOffset Released);

public sealed record AdminUser(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl,
    [property: JsonPropertyName("role")] string? Role);

public sealed record AdminMe(
    [property: JsonPropertyName("user")] AdminUser User);

public sealed record WowInstall(
    string Root,
    string Flavour,
    string FlavourPath,
    string AddOnsPath,
    string? ProductCode,
    string? ClientVersion);

public sealed record InstalledAddonRecord(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("sha256")] string Sha256);

public sealed record AppState(
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("installs")] Dictionary<string, InstalledAddonRecord> Installs,
    [property: JsonPropertyName("session_token")] string? EncryptedSessionToken = null);

[JsonSerializable(typeof(AddonRelease))]
[JsonSerializable(typeof(AdminMe))]
[JsonSerializable(typeof(AdminUser))]
[JsonSerializable(typeof(AppState))]
public sealed partial class CompanionJsonContext : JsonSerializerContext;
