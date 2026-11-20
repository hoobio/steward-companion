using System.Collections.Concurrent;

namespace Steward.Core;

public static class PersonAvatars
{
    public const string FolderName = "Avatars";

    public const int PersonSize = 64;

    public const int DiscordSize = 32;

    private const int MaxConcurrentDownloads = 4;

    public static string RequestUri(Uri source, int size = PersonSize) =>
        new UriBuilder(source) { Path = Path.ChangeExtension(source.AbsolutePath, ".png"), Query = $"size={size}" }.Uri.ToString();

    public static bool IsSafe(KeyValuePair<string, string> image) =>
        image.Key.Length > 0
        && image.Key.All(char.IsAsciiDigit)
        && Uri.TryCreate(image.Value, UriKind.Absolute, out _);

    public static HashSet<string> Wanted(SyncPayload payload) =>
        payload.Images.People.Concat(payload.Images.Discord).Where(IsSafe).Select(image => image.Key)
            .ToHashSet(StringComparer.Ordinal);

    public static string FileNameFor(string userId) => Path.Combine(FolderName, userId + ".tga");

    public static string FolderFor(string addOnsPath) =>
        Path.Combine(addOnsPath, StewardSavedVariables.AddonName, FolderName);

    public static string PathFor(string addOnsPath, string userId) =>
        Path.Combine(addOnsPath, StewardSavedVariables.AddonName, FileNameFor(userId));

    public static string TexturePathFor(string userId) =>
        $@"Interface\AddOns\{StewardSavedVariables.AddonName}\{FolderName}\{userId}.tga";

    public static bool NeedsWrite(string addOnsPath, string userId, AvatarImage image, IReadOnlyDictionary<string, string>? index) =>
        !File.Exists(PathFor(addOnsPath, userId))
        || !string.Equals(index?.GetValueOrDefault(userId), image.SourceUrl, StringComparison.Ordinal);

    public static bool HasPendingWrite(string addOnsPath, SyncPayload payload, IReadOnlyDictionary<string, string>? index) =>
        payload.Avatars.Any(entry => NeedsWrite(addOnsPath, entry.Key, entry.Value, index));

    public static void Record(AppState state, WowInstall install, SyncPayload payload)
    {
        var previous = state.PersonAvatars.GetValueOrDefault(install.FlavourPath);
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var userId in Wanted(payload))
        {
            var url = payload.Avatars.GetValueOrDefault(userId)?.SourceUrl ?? previous?.GetValueOrDefault(userId);
            if (url is not null && File.Exists(PathFor(install.AddOnsPath, userId)))
            {
                current[userId] = url;
            }
        }

        state.PersonAvatars[install.FlavourPath] = current;
    }

    public static async Task<IReadOnlyDictionary<string, AvatarImage>> LoadAsync(
        SyncImages images,
        IReadOnlyCollection<WowInstall> installs,
        AppStateStore stateStore,
        IReadOnlyDictionary<string, AvatarImage>? previous,
        Func<string, int, CancellationToken, Task<AvatarImage?>> download,
        CancellationToken cancellationToken)
    {
        var sources = images.People.Where(IsSafe).Select(image => (image.Key, image.Value, Size: PersonSize))
            .Concat(images.Discord.Where(image => IsSafe(image) && !images.People.ContainsKey(image.Key)).Select(image => (image.Key, image.Value, Size: DiscordSize)));
        var state = stateStore.Load();
        var loaded = new ConcurrentDictionary<string, AvatarImage>(StringComparer.Ordinal);
        var wanted = new List<(string Id, string Url, int Size)>();
        foreach (var (id, url, size) in sources)
        {
            var request = RequestUri(new Uri(url), size);
            if (previous?.GetValueOrDefault(id) is { } known && string.Equals(known.SourceUrl, request, StringComparison.Ordinal))
            {
                loaded[id] = known;
            }
            else if (installs.Any(install => !install.IsMissing && IsStale(state, install, id, request)))
            {
                wanted.Add((id, url, size));
            }
        }

        await Parallel.ForEachAsync(
            wanted,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentDownloads, CancellationToken = cancellationToken },
            async (source, token) =>
            {
                if (await download(source.Url, source.Size, token).ConfigureAwait(false) is { } image)
                {
                    loaded[source.Id] = image;
                }
            }).ConfigureAwait(false);

        return loaded;
    }

    private static bool IsStale(AppState state, WowInstall install, string userId, string request) =>
        !string.Equals(state.PersonAvatars.GetValueOrDefault(install.FlavourPath)?.GetValueOrDefault(userId), request, StringComparison.Ordinal)
        || !File.Exists(PathFor(install.AddOnsPath, userId));
}
