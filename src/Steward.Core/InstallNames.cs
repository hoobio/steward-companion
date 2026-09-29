namespace Steward.Core;

public static class InstallNames
{
    public static IReadOnlyList<string> Resolve(IEnumerable<(string? UserLabel, string DefaultName)> installs)
    {
        ArgumentNullException.ThrowIfNull(installs);

        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return [.. installs.Select(install =>
        {
            if (install.UserLabel is { } label)
            {
                return label;
            }

            var count = seen[install.DefaultName] = seen.GetValueOrDefault(install.DefaultName) + 1;
            return count == 1 ? install.DefaultName : $"{install.DefaultName} ({count})";
        })];
    }
}
