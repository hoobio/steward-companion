using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Steward.Core;

public sealed record JourneySummary(long Rows, long? First, long? Last, string? CharacterGuid);

public sealed record JourneyFileRef(string Path, string Account, string Realm, string Character);

public sealed record JourneyUploadResponse(
    [property: JsonPropertyName("result")] string Result,
    [property: JsonPropertyName("sha256")] string? Sha256);

public sealed record JourneyUploadRecord(
    [property: JsonPropertyName("sha256")] string? Sha256,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("mtime")] DateTime MtimeUtc,
    [property: JsonPropertyName("result")] string? Result,
    [property: JsonPropertyName("uploaded_at")] DateTimeOffset? UploadedAt,
    [property: JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error = null,
    [property: JsonPropertyName("error_at"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ErrorAt = null,
    [property: JsonPropertyName("rejected_sha256"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RejectedSha256 = null);

public sealed record JourneyUploadsState(
    [property: JsonPropertyName("journey_uploads")] Dictionary<string, JourneyUploadRecord> JourneyUploads);

public static partial class JourneyFile
{
    public const string FileName = "StewardJourney.lua";

    public static IReadOnlyList<JourneyFileRef> Find(string flavourPath)
    {
        var root = Path.Combine(flavourPath, "WTF", "Account");
        var found = new List<JourneyFileRef>();
        try
        {
            if (!Directory.Exists(root))
            {
                return found;
            }

            foreach (var account in Directory.EnumerateDirectories(root))
            {
                foreach (var realm in Directory.EnumerateDirectories(account))
                {
                    foreach (var character in Directory.EnumerateDirectories(realm))
                    {
                        var path = Path.Combine(character, "SavedVariables", FileName);
                        if (File.Exists(path))
                        {
                            found.Add(new JourneyFileRef(path, Path.GetFileName(account), Path.GetFileName(realm), Path.GetFileName(character)));
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return found;
    }

    public static string AccountHash(string accountFolder) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(accountFolder)))[..16];

    // Latin1 maps every byte to one char, so the gzip chunks inside Lua strings survive the existing text parser intact.
    public static JourneySummary Summarise(byte[] bytes)
    {
        var globals = LuaSavedVariables.Parse(Encoding.Latin1.GetString(bytes));
        if (!globals.TryGetValue("StewardJourneyDB", out var db) || db.Kind != LuaKind.Table)
        {
            return new JourneySummary(0, null, null, null);
        }

        var route = db.GetTable("route")?.Items.Where(row => row.Kind == LuaKind.Table).ToList() ?? [];
        var packed = db.GetTable("packed")?.Items.Where(chunk => chunk.Kind == LuaKind.Table).ToList() ?? [];
        var rows = route.Count + packed.Sum(chunk => (long)(chunk.GetNumber("rows") ?? 0));
        var starts = route.Select(row => row.GetNumber("t")).Concat(packed.Select(chunk => chunk.GetNumber("from"))).OfType<double>().ToList();
        var ends = route.Select(row => row.GetNumber("t")).Concat(packed.Select(chunk => chunk.GetNumber("to"))).OfType<double>().ToList();
        var guid = route.LastOrDefault(row => row.GetString("ev") == "login" && row.GetString("guid") is { Length: > 0 })?.GetString("guid")
            ?? PackedGuid(packed.OrderBy(chunk => chunk.GetNumber("to") ?? 0).LastOrDefault());
        return new JourneySummary(
            rows,
            starts.Count == 0 ? null : (long)starts.Min(),
            ends.Count == 0 ? null : (long)ends.Max(),
            guid);
    }

    private static string? PackedGuid(LuaValue? chunk)
    {
        if (chunk?.GetString("data") is not { } data)
        {
            return null;
        }

        try
        {
            using var gzip = new GZipStream(new MemoryStream(Encoding.Latin1.GetBytes(data)), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            var matches = GuidPattern().Matches(reader.ReadToEnd());
            return matches.Count == 0 ? null : matches[^1].Groups[1].Value;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return null;
        }
    }

    [GeneratedRegex("\"guid\"\\s*:\\s*\"(Player-[^\"]+)\"")]
    private static partial Regex GuidPattern();
}

public enum JourneyPassResult
{
    Done,
    SessionExpired,
}

public sealed class JourneyUploader(StewardClient client, AppStateStore store, Func<DateTimeOffset>? clock = null)
{
    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromMinutes(1);

    private readonly Func<DateTimeOffset> _now = clock ?? (() => DateTimeOffset.Now);
    private DateTimeOffset _retryAt;

    public bool IsBlocked { get; private set; }

    public void Unblock() => IsBlocked = false;

    private enum FileResult
    {
        Next,
        Halt,
        SessionExpired,
    }

    public async Task<JourneyPassResult> UploadAsync(IEnumerable<string> flavourPaths, CancellationToken cancellationToken)
    {
        foreach (var file in flavourPaths.SelectMany(JourneyFile.Find))
        {
            if (IsBlocked || _now() < _retryAt)
            {
                return JourneyPassResult.Done;
            }

            switch (await UploadAsync(file, cancellationToken).ConfigureAwait(false))
            {
                case FileResult.SessionExpired:
                    return JourneyPassResult.SessionExpired;
                case FileResult.Halt:
                    return JourneyPassResult.Done;
            }
        }

        return JourneyPassResult.Done;
    }

    private async Task<FileResult> UploadAsync(JourneyFileRef file, CancellationToken cancellationToken)
    {
        var info = new FileInfo(file.Path);
        if (!info.Exists)
        {
            return FileResult.Next;
        }

        var last = store.Load().JourneyUploads.GetValueOrDefault(file.Path);
        if (last is { Sha256: not null } && last.Size == info.Length && last.MtimeUtc == info.LastWriteTimeUtc)
        {
            return FileResult.Next;
        }

        byte[] bytes;
        try
        {
            using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FileResult.Next;
        }

        if (bytes.Length != info.Length)
        {
            return FileResult.Next;
        }

        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (last?.Sha256 == sha)
        {
            Save(file.Path, record => record with { Size = info.Length, MtimeUtc = info.LastWriteTimeUtc });
            return FileResult.Next;
        }

        if (last?.RejectedSha256 == sha)
        {
            return FileResult.Next;
        }

        JourneySummary summary;
        try
        {
            summary = JourneyFile.Summarise(bytes);
        }
        catch (FormatException ex)
        {
            Save(file.Path, record => record with { Error = ex.Message, ErrorAt = _now(), RejectedSha256 = sha });
            return FileResult.Next;
        }

        if (summary.Rows == 0)
        {
            return FileResult.Next;
        }

        try
        {
            var response = await client.PutJourneyAsync(
                JourneyFile.AccountHash(file.Account),
                file.Realm,
                file.Character,
                bytes,
                summary,
                new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(),
                cancellationToken).ConfigureAwait(false);
            Save(file.Path, _ => new JourneyUploadRecord(sha, info.Length, info.LastWriteTimeUtc, response.Result, _now()));
            return FileResult.Next;
        }
        catch (SessionExpiredException)
        {
            return FileResult.SessionExpired;
        }
        catch (StewardThrottledException ex)
        {
            _retryAt = _now() + (ex.RetryAfter ?? DefaultRetryAfter);
            Save(file.Path, record => record with { Error = "Sent too recently, trying again shortly", ErrorAt = _now() });
            return FileResult.Halt;
        }
        catch (StewardRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            IsBlocked = true;
            Save(file.Path, record => record with
            {
                Error = ex.StatusCode == HttpStatusCode.Forbidden ? "Your account can't upload journeys." : "Steward does not take journey uploads yet.",
                ErrorAt = _now(),
            });
            return FileResult.Halt;
        }
        catch (StewardRequestException ex)
        {
            Save(file.Path, record => record with
            {
                Error = ex.StatusCode == HttpStatusCode.RequestEntityTooLarge ? "The journey file is too large to upload." : ex.Body ?? ex.Message,
                ErrorAt = _now(),
                RejectedSha256 = sha,
            });
            return FileResult.Next;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Save(file.Path, record => record with { Error = "Steward unreachable, retrying", ErrorAt = _now() });
            return FileResult.Halt;
        }
    }

    private void Save(string path, Func<JourneyUploadRecord, JourneyUploadRecord> update)
    {
        var state = store.Load();
        state.JourneyUploads[path] = update(state.JourneyUploads.GetValueOrDefault(path) ?? new JourneyUploadRecord(null, 0, default, null, null));
        store.Save(state);
    }
}
