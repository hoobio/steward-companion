using System.Text;

namespace Steward.Core;

public static class AddonThemeFile
{
    public const string RelativePath = "Core/Theme.lua";

    public static bool AppliesTo(ManagedAddon addon) =>
        addon.Notice is not null
        || string.Equals(addon.ManifestAuth, "session", StringComparison.OrdinalIgnoreCase)
        || addon.Source == AddonCatalogue.ProtectedSource;

    public static string Render(string themeId) =>
        $"local _, ns = ...\nns.themeId = \"{themeId.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"\n";

    public static bool Write(string addOnsPath, string folder, string? themeId)
    {
        if (string.IsNullOrEmpty(themeId) || string.IsNullOrEmpty(folder))
        {
            return false;
        }

        if (folder is "." or ".." || !string.Equals(Path.GetFileName(folder), folder, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"refusing a folder name that is not a single path segment: {folder}");
        }

        var addonRoot =Path.GetFullPath(Path.Combine(addOnsPath, folder) + Path.DirectorySeparatorChar);
        var target = Path.GetFullPath(Path.Combine(addonRoot, RelativePath));
        if (!target.StartsWith(addonRoot, StringComparison.OrdinalIgnoreCase)
            || target.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("WTF", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"refusing to write outside the addon folder: {target}");
        }

        var contents = new UTF8Encoding(false).GetBytes(Render(themeId));
        lock (AddonUpdater.AddOnsWriteLock)
        {
            if (!File.Exists(target) || File.ReadAllBytes(target).AsSpan().SequenceEqual(contents))
            {
                return false;
            }

            var tempPath = target + ".tmp";
            File.WriteAllBytes(tempPath, contents);
            File.Move(tempPath, target, overwrite: true);
            return true;
        }
    }
}
