namespace Steward.Core;

public sealed record AddonChannelStatus(
    IReadOnlyDictionary<string, AddonRelease?> Releases,
    string? Channel,
    string? Notice)
{
    public static readonly IReadOnlyList<string> Ordered = ["release", "pre-release", "develop"];

    public static readonly IReadOnlyList<string> DefaultPreference = Ordered;

    public static readonly IReadOnlyList<string> GitHubChannels = ["release", "pre-release"];

    public AddonRelease? Release => Channel is null ? null : Releases.GetValueOrDefault(Channel);

    public bool Has(string channel) => Releases.GetValueOrDefault(channel) is not null;

    public static AddonChannelStatus Resolve(
        string? stored,
        IReadOnlyDictionary<string, AddonRelease?> releases,
        IReadOnlyList<string> ordered,
        IReadOnlyList<string> preference)
    {
        var best = preference.FirstOrDefault(c => releases.GetValueOrDefault(c) is not null);
        if (stored is not null && releases.GetValueOrDefault(stored) is not null)
        {
            return new AddonChannelStatus(releases, stored, NewerStableNotice(releases, ordered, stored));
        }
        if (stored is not null && releases.ContainsKey(stored) && best is not null)
        {
            return new AddonChannelStatus(releases, best, $"No releases on {stored} any more. Showing {best}.");
        }
        return new AddonChannelStatus(releases, best, NewerStableNotice(releases, ordered, best));
    }

    private static string? NewerStableNotice(IReadOnlyDictionary<string, AddonRelease?> releases, IReadOnlyList<string> ordered, string? chosen)
    {
        if (chosen is null || releases.GetValueOrDefault(chosen) is not { } current)
        {
            return null;
        }

        var newer = ordered
            .TakeWhile(c => !string.Equals(c, chosen, StringComparison.OrdinalIgnoreCase))
            .Select(c => (Channel: c, Release: releases.GetValueOrDefault(c)))
            .FirstOrDefault(pair => pair.Release is not null && pair.Release.Released > current.Released);
        return newer.Release is null ? null : $"{newer.Channel} has a newer build, {newer.Release.Version}. Switch channel to install it.";
    }
}
