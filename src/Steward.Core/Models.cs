using System.Text.Json;
using System.Text.Json.Serialization;

namespace Steward.Core;

public sealed record ManagedAddon(string Id, string FolderName, string? ManifestBaseUrl = null, bool AutoInstall = false, string? Name = null, IReadOnlyList<string>? Features = null)
{
    public IReadOnlyList<string> Features { get; init; } = Features ?? [GigagrugClient.AddonsFeature];

    public string DisplayName => Name ?? FolderName;

    public IReadOnlyList<string> Channels { get; } = AddonChannelStatus.Ordered;

    public IReadOnlyList<string> DefaultPreference { get; } = AddonChannelStatus.DefaultPreference;

    public Uri IconUri => new(new Uri(ManifestBaseUrl ?? throw new InvalidOperationException($"{Id} has no ManifestBaseUrl")), "icon.png");
}

public sealed record AddonRelease(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("zip")] string Zip,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("released")] DateTimeOffset Released);

public sealed record AdminUser(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("features")] IReadOnlyList<string>? Features = null);

public sealed record AdminGuild(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("icon_url")] string? IconUrl,
    [property: JsonPropertyName("member_count")] int MemberCount,
    [property: JsonPropertyName("nick")] string? Nick);

public sealed record AdminMe(
    [property: JsonPropertyName("user")] AdminUser User,
    [property: JsonPropertyName("guilds")] IReadOnlyList<AdminGuild> Guilds)
{
    public AdminGuild? ResolveGuild(string? guildId) =>
        Guilds.FirstOrDefault(guild => string.Equals(guild.Id, guildId, StringComparison.Ordinal))
        ?? (Guilds.Count > 0 ? Guilds[0] : null);
}

public sealed record DesktopExchangeRequest(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("verifier")] string Verifier);

public sealed record DesktopToken(
    [property: JsonPropertyName("token")] string Token);

public sealed record RestedXpTokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken = null,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken = null,
    [property: JsonPropertyName("expires_in")] int ExpiresIn = 0,
    [property: JsonPropertyName("refresh_expires_in")] int RefreshExpiresIn = 0);

public sealed record RestedXpKeycloakError(
    [property: JsonPropertyName("error")] string? Error = null,
    [property: JsonPropertyName("error_description")] string? ErrorDescription = null);

public sealed record RestedXpTokens(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessExpiresAt,
    DateTimeOffset? RefreshExpiresAt);

public sealed record RestedXpCookie(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("refreshToken")] string RefreshToken,
    [property: JsonPropertyName("user")] Dictionary<string, string> User,
    [property: JsonPropertyName("expiresAt")] long ExpiresAt);

public sealed record RestedXpProduct(
    [property: JsonPropertyName("productName")] string ProductName,
    [property: JsonPropertyName("productImageUrl")] string? ProductImageUrl);

public sealed record RestedXpTimestamps(
    [property: JsonPropertyName("timestamps")] Dictionary<string, long> Timestamps);

public sealed record RestedXpGuide(
    [property: JsonPropertyName("guideName")] string GuideName,
    [property: JsonPropertyName("guide")] string Guide,
    [property: JsonPropertyName("bnetTag")] string? BnetTag);

public sealed record RestedXpGuideResponse(
    [property: JsonPropertyName("encryptedGuides")] RestedXpGuide[] EncryptedGuides);

public sealed record RestedXpSession(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("access_expires_at")] DateTimeOffset AccessExpiresAt,
    [property: JsonPropertyName("refresh_expires_at")] DateTimeOffset? RefreshExpiresAt);

public sealed record RestedXpCachedGuide(
    [property: JsonPropertyName("timestamp")] long Timestamp,
    [property: JsonPropertyName("bnet_tag")] string? BnetTag);

public sealed record RestedXpGuideRecord(
    [property: JsonPropertyName("timestamp")] long Timestamp,
    [property: JsonPropertyName("written_at")] DateTimeOffset WrittenAt);

public enum AutoUpdateMode
{
    Always,
    OutOfGame,
    Never,
}

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
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("installed_at")] DateTimeOffset? InstalledAt = null);

public sealed record AppState(
    [property: JsonPropertyName("channels")] Dictionary<string, string> Channels,
    [property: JsonPropertyName("installs")] Dictionary<string, InstalledAddonRecord> Installs,
    [property: JsonPropertyName("session_token")] string? EncryptedSessionToken = null,
    [property: JsonPropertyName("added_installs")] List<string> AddedInstalls = null!,
    [property: JsonPropertyName("keep_in_tray")] bool MinimizeToTray = true,
    [property: JsonPropertyName("hidden_addons")] List<string> HiddenAddons = null!,
    [property: JsonPropertyName("restedxp_session")] string? EncryptedRestedXpSession = null,
    [property: JsonPropertyName("restedxp_guides")] Dictionary<string, RestedXpGuideRecord> RestedXpGuides = null!,
    [property: JsonPropertyName("restedxp_guide_choices")] Dictionary<string, List<string>> RestedXpGuideChoices = null!,
    [property: JsonPropertyName("restedxp_guides_generation")] Dictionary<string, long> RestedXpGuidesGeneration = null!,
    [property: JsonPropertyName("guild_roster_sync")] Dictionary<string, string> GuildRosterSync = null!,
    [property: JsonPropertyName("guild_id")] string? GuildId = null,
    [property: JsonPropertyName("close_to_tray")] bool CloseToTray = false,
    [property: JsonPropertyName("auto_update")] string AutoUpdate = "out-of-game")
{
    [JsonPropertyName("channel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyChannel { get; init; }

    [JsonPropertyName("restedxp_guide_choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? LegacyRestedXpGuideChoice { get; init; }
}

[JsonSerializable(typeof(AddonRelease))]
[JsonSerializable(typeof(AdminMe))]
[JsonSerializable(typeof(AdminUser))]
[JsonSerializable(typeof(AppState))]
[JsonSerializable(typeof(DesktopExchangeRequest))]
[JsonSerializable(typeof(DesktopToken))]
[JsonSerializable(typeof(DiscordMembersResponse))]
[JsonSerializable(typeof(GuildRosterResponse))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(RestedXpCachedGuide))]
[JsonSerializable(typeof(RestedXpCookie))]
[JsonSerializable(typeof(RestedXpGuideResponse))]
[JsonSerializable(typeof(RestedXpKeycloakError))]
[JsonSerializable(typeof(RestedXpProduct[]))]
[JsonSerializable(typeof(RestedXpSession))]
[JsonSerializable(typeof(RestedXpTokenResponse))]
[JsonSerializable(typeof(RestedXpTimestamps))]
public sealed partial class CompanionJsonContext : JsonSerializerContext;
