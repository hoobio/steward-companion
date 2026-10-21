using System.Text.RegularExpressions;

using Microsoft.Win32;

namespace Steward.Core;

public static partial class BattleNet
{
    private const string MacApp = "/Applications/Battle.net.app";

    // A battlenet:// link leaves a tray-minimised Battle.net hidden; starting the exe again restores its window (verified 14 Oct 2026).
    public static string? Launcher()
    {
        if (OperatingSystem.IsWindows())
        {
            return (Registry.ClassesRoot.OpenSubKey(@"battlenet\shell\open\command")?.GetValue(null) as string)
                ?.Split('"', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() is { } exe && File.Exists(exe) ? exe : null;
        }

        if (OperatingSystem.IsMacOS())
        {
            return Directory.Exists(MacApp) ? MacApp : null;
        }

        return LutrisLink(WinePrefixes.LutrisConfigs(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable));
    }

    public static string CacheFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Battle.net", "Cache");

    // "--exec=launch <family>" starts the family's default product type, which Battle.net's catalogue cache records as default_product_type, once per region's catalogue (read 21 Oct 2026).
    public static IReadOnlySet<string> FamilyDefaults(string cacheFolder, string family)
    {
        var defaults = new HashSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(cacheFolder))
        {
            return defaults;
        }

        var marker = $"\"id\":\"{family}\"";
        foreach (var file in Directory.EnumerateFiles(cacheFolder, "*", SearchOption.AllDirectories).Where(file => new FileInfo(file).Length < 8_000_000))
        {
            try
            {
                var text = File.ReadAllText(file);
                if (text.Contains(marker, StringComparison.Ordinal))
                {
                    using var document = System.Text.Json.JsonDocument.Parse(text);
                    CollectDefaults(document.RootElement, family, defaults);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
            }
        }

        return defaults;
    }

    private static void CollectDefaults(System.Text.Json.JsonElement element, string family, HashSet<string> defaults)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (element.TryGetProperty("id", out var id) && id.ValueKind == System.Text.Json.JsonValueKind.String && id.GetString() == family
                && element.TryGetProperty("base", out var familyBase) && familyBase.ValueKind == System.Text.Json.JsonValueKind.Object
                && familyBase.TryGetProperty("default_product_type", out var product) && product.GetString() is { Length: > 0 } type)
            {
                defaults.Add(type);
            }

            foreach (var property in element.EnumerateObject())
            {
                CollectDefaults(property.Value, family, defaults);
            }
        }
        else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectDefaults(item, family, defaults);
            }
        }
    }

    internal static string? LutrisLink(IEnumerable<string> configs) =>
        configs.Any(config => LutrisSlug().Replace(Path.GetFileNameWithoutExtension(config), string.Empty) == "battlenet")
            ? "lutris:rungame/battlenet"
            : null;

    [GeneratedRegex(@"-\d+$")]
    private static partial Regex LutrisSlug();
}
