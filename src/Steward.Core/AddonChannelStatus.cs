namespace Steward.Core;

public sealed record AddonChannelStatus(
    IReadOnlyDictionary<string, AddonRelease?> Releases,
    string? Channel,
    string? Notice)
{
    public static readonly IReadOnlyList<string> Ordered = ["stable", "beta", "unstable"];

    public AddonRelease? Release => Channel is null ? null : Releases.GetValueOrDefault(Channel);

    public bool Has(string channel) => Releases.GetValueOrDefault(channel) is not null;

    public static AddonChannelStatus Resolve(string? stored, IReadOnlyDictionary<string, AddonRelease?> releases)
    {
        var best = Ordered.FirstOrDefault(c => releases.GetValueOrDefault(c) is not null);
        if (stored is not null && releases.GetValueOrDefault(stored) is not null)
        {
            return new AddonChannelStatus(releases, stored, null);
        }
        if (stored is not null && best is not null)
        {
            return new AddonChannelStatus(releases, best, $"No releases on {stored} any more. Showing {best}.");
        }
        return new AddonChannelStatus(releases, best, null);
    }
}
