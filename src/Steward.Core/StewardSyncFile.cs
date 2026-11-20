using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace Steward.Core;

public static class StewardSyncFile
{
    public const string FileName = "StewardSync.lua";

    public const string AvatarFileName = "Avatar.tga";

    private static readonly string AvatarTexturePath =
        $@"Interface\AddOns\{StewardSavedVariables.AddonName}\{AvatarFileName}";

    public static string PathFor(string addOnsPath) =>
        Path.Combine(addOnsPath, StewardSavedVariables.AddonName, FileName);

    public static string AvatarPathFor(string addOnsPath) =>
        Path.Combine(addOnsPath, StewardSavedVariables.AddonName, AvatarFileName);

    private const string FingerprintKey = "[\"fingerprint\"] = \"";

    public static string Render(SyncPayload payload, bool withAvatar = false, string? fingerprint = null, IReadOnlySet<string>? avatarIds = null) =>
        "Steward.LoadSync(" + LuaWriter.Serialize(ToLua(payload, withAvatar, fingerprint, avatarIds)) + ")" + Environment.NewLine;

    public static string Fingerprint(SyncPayload payload) =>
        HashUtf8(LuaWriter.Serialize(LuaValue.FromTable(payload.Entries)), payload.Avatar?.SourceUrl, ImageUrls(payload.Images.People), ImageUrls(payload.Images.Discord));

    private static string ImageUrls(IReadOnlyDictionary<string, string> urls) => string.Concat(
        urls.Where(PersonAvatars.IsSafe)
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => $"{entry.Key}={entry.Value}\n"));

    private static string HashUtf8(params string?[] parts)
    {
        const int ChunkChars = 4096;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(ChunkChars));
        try
        {
            foreach (var part in parts)
            {
                var chars = part.AsSpan();
                while (!chars.IsEmpty)
                {
                    var take = Math.Min(ChunkChars, chars.Length);
                    if (take < chars.Length && char.IsHighSurrogate(chars[take - 1]))
                    {
                        take--;
                    }

                    hash.AppendData(buffer, 0, Encoding.UTF8.GetBytes(chars[..take], buffer));
                    chars = chars[take..];
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static string? ReadFingerprint(string addOnsPath)
    {
        try
        {
            using var reader = new StreamReader(PathFor(addOnsPath), Encoding.UTF8);
            var buffer = new char[512];
            var head = new string(buffer, 0, reader.ReadBlock(buffer));
            var start = head.IndexOf(FingerprintKey, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            start += FingerprintKey.Length;
            var end = head.IndexOf('"', start);
            return end < 0 ? null : head[start..end];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Write(string addOnsPath, SyncPayload payload, string? fingerprint = null, IReadOnlyDictionary<string, string>? avatarIndex = null)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var tocPath = Path.Combine(addOnsPath, StewardSavedVariables.AddonName, $"{StewardSavedVariables.AddonName}.toc");
        lock (AddonUpdater.AddOnsWriteLock)
        {
            if (!File.Exists(tocPath))
            {
                throw new InvalidOperationException(
                    $"{tocPath} does not exist; the Steward addon must be installed before writing {FileName}");
            }

            var wroteAvatar = TryWriteAvatar(addOnsPath, payload.Avatar);
            var avatarIds = SyncPersonAvatars(addOnsPath, payload, avatarIndex);
            WriteGuarded(addOnsPath, FileName, Encoding.UTF8.GetBytes(Render(payload, wroteAvatar, fingerprint ?? Fingerprint(payload), avatarIds)));
        }
    }

    private static HashSet<string> SyncPersonAvatars(string addOnsPath, SyncPayload payload, IReadOnlyDictionary<string, string>? index)
    {
        var wanted = PersonAvatars.Wanted(payload);
        foreach (var (userId, image) in payload.Avatars)
        {
            if (!wanted.Contains(userId) || !PersonAvatars.NeedsWrite(addOnsPath, userId, image, index))
            {
                continue;
            }

            try
            {
                WriteGuarded(addOnsPath, PersonAvatars.FileNameFor(userId), AvatarTga.Encode(image));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
            }
        }

        var folder = PersonAvatars.FolderFor(addOnsPath);
        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.tga"))
            {
                if (wanted.Contains(Path.GetFileNameWithoutExtension(file)))
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        wanted.RemoveWhere(userId => !File.Exists(PersonAvatars.PathFor(addOnsPath, userId)));
        return wanted;
    }

    private static bool TryWriteAvatar(string addOnsPath, AvatarImage? avatar)
    {
        if (avatar is null)
        {
            return false;
        }

        try
        {
            WriteGuarded(addOnsPath, AvatarFileName, AvatarTga.Encode(avatar));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void WriteGuarded(string addOnsPath, string fileName, byte[] contents)
    {
        var addonRoot = Path.GetFullPath(
            Path.Combine(addOnsPath, StewardSavedVariables.AddonName) + Path.DirectorySeparatorChar);
        var target = Path.GetFullPath(Path.Combine(addOnsPath, StewardSavedVariables.AddonName, fileName));
        if (!target.StartsWith(addonRoot, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(target), Path.GetFileName(fileName), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"refusing to write outside the Steward addon folder: {target}");
        }

        if (target.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains("WTF", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"refusing to write a path containing a WTF segment: {target}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var tempPath = target + ".tmp";
        File.WriteAllBytes(tempPath, contents);
        File.Move(tempPath, target, overwrite: true);
    }

    private static LuaValue ToLua(SyncPayload payload, bool withAvatar, string? fingerprint, IReadOnlySet<string>? avatarIds)
    {
        var entries = new List<LuaEntry>
        {
            new(LuaValue.FromString("writtenAt"), LuaValue.FromNumber(payload.WrittenAt.ToUnixTimeSeconds())),
        };

        if (fingerprint is not null)
        {
            entries.Add(new(LuaValue.FromString("fingerprint"), LuaValue.FromString(fingerprint)));
        }

        if (withAvatar)
        {
            // GetTexture() echoes back any path it was given and a texture that failed to load draws solid green, so the key's presence is the addon's only signal the file is really there.
            entries.Add(new(LuaValue.FromString("avatar"), LuaValue.FromString(AvatarTexturePath)));
        }

        entries.AddRange(avatarIds is { Count: > 0 } ? payload.Entries.Select(entry => WithAvatars(entry, avatarIds)) : payload.Entries);
        return LuaValue.FromTable(entries);
    }

    private static LuaEntry WithAvatars(LuaEntry entry, IReadOnlySet<string> avatarIds) => entry.Key?.Text switch
    {
        "discord" => entry with { Value = AvatarsOn(entry.Value, avatarIds) },
        "directory" when entry.Value.Kind == LuaKind.Table => entry with
        {
            Value = LuaValue.FromTable([.. entry.Value.Table.Select(field => field.Key?.Text == "people" ? field with { Value = AvatarsOn(field.Value, avatarIds) } : field)]),
        },
        _ => entry,
    };

    private static LuaValue AvatarsOn(LuaValue list, IReadOnlySet<string> avatarIds) => list.Kind != LuaKind.Table
        ? list
        : LuaValue.FromTable([.. list.Table.Select(item => item.Key is null && item.Value.GetString("id") is { } id && avatarIds.Contains(id)
            ? item with { Value = LuaValue.FromTable([.. item.Value.Table, new LuaEntry(LuaValue.FromString("avatar"), LuaValue.FromString(PersonAvatars.TexturePathFor(id)))]) }
            : item)]);
}
