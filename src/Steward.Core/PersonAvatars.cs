using System.Collections.Concurrent;

namespace Steward.Core;

public static class PersonAvatars
{
    public const string FolderName = "Avatars";

    private const int MaxConcurrentDownloads = 4;

    public static string RequestUri(Uri source) =>
        new UriBuilder(source) { Path = Path.ChangeExtension(source.AbsolutePath, ".png"), Query = "size=64" }.Uri.ToString();

    public static bool IsEligible(DirectoryPerson person) =>
        person.AvatarUrl is not null
        && Uri.TryCreate(person.AvatarUrl, UriKind.Absolute, out _)
        && person.Id.Length > 0
        && person.Id.All(char.IsAsciiDigit);

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

    public static bool HasPendingWrite(string addOnsPath, SyncDirectory? directory, IReadOnlyDictionary<string, string>? index) =>
        directory?.Avatars?.Any(entry => NeedsWrite(addOnsPath, entry.Key, entry.Value, index)) == true;

    public static void Record(AppState state, WowInstall install, SyncDirectory? directory)
    {
        var previous = state.PersonAvatars.GetValueOrDefault(install.FlavourPath);
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var person in directory?.People?.Where(IsEligible) ?? [])
        {
            var url = directory?.Avatars?.GetValueOrDefault(person.Id)?.SourceUrl ?? previous?.GetValueOrDefault(person.Id);
            if (url is not null && File.Exists(PathFor(install.AddOnsPath, person.Id)))
            {
                current[person.Id] = url;
            }
        }

        state.PersonAvatars[install.FlavourPath] = current;
    }

    public static async Task<IReadOnlyDictionary<string, AvatarImage>> LoadAsync(
        IReadOnlyList<DirectoryPerson> people,
        IReadOnlyCollection<WowInstall> installs,
        AppStateStore stateStore,
        IReadOnlyDictionary<string, AvatarImage>? previous,
        Func<string, CancellationToken, Task<AvatarImage?>> download,
        CancellationToken cancellationToken)
    {
        var state = stateStore.Load();
        var images = new ConcurrentDictionary<string, AvatarImage>(StringComparer.Ordinal);
        var wanted = new List<DirectoryPerson>();
        foreach (var person in people.Where(IsEligible))
        {
            var request = RequestUri(new Uri(person.AvatarUrl!));
            if (previous?.GetValueOrDefault(person.Id) is { } known && string.Equals(known.SourceUrl, request, StringComparison.Ordinal))
            {
                images[person.Id] = known;
            }
            else if (installs.Any(install => !install.IsMissing && IsStale(state, install, person.Id, request)))
            {
                wanted.Add(person);
            }
        }

        await Parallel.ForEachAsync(
            wanted,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentDownloads, CancellationToken = cancellationToken },
            async (person, token) =>
            {
                if (await download(person.AvatarUrl!, token).ConfigureAwait(false) is { } image)
                {
                    images[person.Id] = image;
                }
            }).ConfigureAwait(false);

        return images;
    }

    private static bool IsStale(AppState state, WowInstall install, string userId, string request) =>
        !string.Equals(state.PersonAvatars.GetValueOrDefault(install.FlavourPath)?.GetValueOrDefault(userId), request, StringComparison.Ordinal)
        || !File.Exists(PathFor(install.AddOnsPath, userId));
}
