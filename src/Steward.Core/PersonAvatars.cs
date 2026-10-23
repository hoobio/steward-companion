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

    public static bool IsEligible(DirectoryPerson person) =>
        person.AvatarUrl is not null
        && Uri.TryCreate(person.AvatarUrl, UriKind.Absolute, out _)
        && person.Id.Length > 0
        && person.Id.All(char.IsAsciiDigit);

    // The mirror hands a default embed avatar for members with no custom one; those get no file.
    public static bool IsEligible(DiscordMember member) =>
        member.AvatarUrl is not null
        && Uri.TryCreate(member.AvatarUrl, UriKind.Absolute, out var uri)
        && !uri.AbsolutePath.StartsWith("/embed/", StringComparison.Ordinal)
        && member.Id.Length > 0
        && member.Id.All(char.IsAsciiDigit);

    public static HashSet<string> Wanted(SyncPayload payload) =>
        (payload.Directory?.People ?? []).Where(IsEligible).Select(person => person.Id)
            .Concat(payload.Discord.Where(IsEligible).Select(member => member.Id))
            .ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, AvatarImage> Images(SyncPayload payload)
    {
        var images = new Dictionary<string, AvatarImage>(payload.DiscordAvatars ?? new Dictionary<string, AvatarImage>(), StringComparer.Ordinal);
        foreach (var (userId, image) in payload.Directory?.Avatars ?? new Dictionary<string, AvatarImage>())
        {
            images[userId] = image;
        }

        return images;
    }

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
        Images(payload).Any(entry => NeedsWrite(addOnsPath, entry.Key, entry.Value, index));

    public static void Record(AppState state, WowInstall install, SyncPayload payload)
    {
        var previous = state.PersonAvatars.GetValueOrDefault(install.FlavourPath);
        var images = Images(payload);
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var userId in Wanted(payload))
        {
            var url = images.GetValueOrDefault(userId)?.SourceUrl ?? previous?.GetValueOrDefault(userId);
            if (url is not null && File.Exists(PathFor(install.AddOnsPath, userId)))
            {
                current[userId] = url;
            }
        }

        state.PersonAvatars[install.FlavourPath] = current;
    }

    public static Task<IReadOnlyDictionary<string, AvatarImage>> LoadAsync(
        IReadOnlyList<DirectoryPerson> people,
        IReadOnlyCollection<WowInstall> installs,
        AppStateStore stateStore,
        IReadOnlyDictionary<string, AvatarImage>? previous,
        Func<string, CancellationToken, Task<AvatarImage?>> download,
        CancellationToken cancellationToken) =>
        LoadAsync(people.Where(IsEligible).Select(person => (person.Id, person.AvatarUrl!)), PersonSize, installs, stateStore, previous, (url, _, token) => download(url, token), cancellationToken);

    public static Task<IReadOnlyDictionary<string, AvatarImage>> LoadDiscordAsync(
        IReadOnlyList<DiscordMember> members,
        IReadOnlyList<DirectoryPerson>? people,
        IReadOnlyCollection<WowInstall> installs,
        AppStateStore stateStore,
        IReadOnlyDictionary<string, AvatarImage>? previous,
        Func<string, int, CancellationToken, Task<AvatarImage?>> download,
        CancellationToken cancellationToken)
    {
        var personIds = (people ?? []).Where(IsEligible).Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        var sources = members.Where(member => IsEligible(member) && !personIds.Contains(member.Id)).Select(member => (member.Id, member.AvatarUrl!));
        return LoadAsync(sources, DiscordSize, installs, stateStore, previous, download, cancellationToken);
    }

    private static async Task<IReadOnlyDictionary<string, AvatarImage>> LoadAsync(
        IEnumerable<(string Id, string Url)> sources,
        int size,
        IReadOnlyCollection<WowInstall> installs,
        AppStateStore stateStore,
        IReadOnlyDictionary<string, AvatarImage>? previous,
        Func<string, int, CancellationToken, Task<AvatarImage?>> download,
        CancellationToken cancellationToken)
    {
        var state = stateStore.Load();
        var images = new ConcurrentDictionary<string, AvatarImage>(StringComparer.Ordinal);
        var wanted = new List<(string Id, string Url)>();
        foreach (var source in sources)
        {
            var request = RequestUri(new Uri(source.Url), size);
            if (previous?.GetValueOrDefault(source.Id) is { } known && string.Equals(known.SourceUrl, request, StringComparison.Ordinal))
            {
                images[source.Id] = known;
            }
            else if (installs.Any(install => !install.IsMissing && IsStale(state, install, source.Id, request)))
            {
                wanted.Add(source);
            }
        }

        await Parallel.ForEachAsync(
            wanted,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentDownloads, CancellationToken = cancellationToken },
            async (source, token) =>
            {
                if (await download(source.Url, size, token).ConfigureAwait(false) is { } image)
                {
                    images[source.Id] = image;
                }
            }).ConfigureAwait(false);

        return images;
    }

    private static bool IsStale(AppState state, WowInstall install, string userId, string request) =>
        !string.Equals(state.PersonAvatars.GetValueOrDefault(install.FlavourPath)?.GetValueOrDefault(userId), request, StringComparison.Ordinal)
        || !File.Exists(PathFor(install.AddOnsPath, userId));
}
