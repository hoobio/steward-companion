using System.Text.Json;

namespace Steward.Core;

public static class AddonCatalogue
{
    public const string StewardSource = "Steward";

    public const string ProtectedSource = "Protected";

    public static IReadOnlyList<ManagedAddon> Visible(
        IReadOnlyList<ManagedAddon>? server, IReadOnlyList<ManagedAddon> configured, IReadOnlySet<string> features) =>
        server ?? [.. configured.Where(addon => addon.Features.Any(features.Contains))];

    public static bool UpdatesWhileUnreachable(IReadOnlyList<ManagedAddon>? server, string addonId) =>
        server?.Any(addon => string.Equals(addon.Id, addonId, StringComparison.OrdinalIgnoreCase) && addon.MayUpdateWhileUnreachable) == true;

    public static bool IsComplete(CatalogueAddon? addon) =>
        addon is { Id.Length: > 0, FolderName.Length: > 0, ManifestBaseUrl.Length: > 0 };

    public static bool Same(IReadOnlyList<CatalogueAddon>? left, IReadOnlyList<CatalogueAddon>? right) =>
        Json(left) == Json(right);

    private static string Json(IReadOnlyList<CatalogueAddon>? catalogue) =>
        JsonSerializer.Serialize(catalogue, CompanionJsonContext.Default.IReadOnlyListCatalogueAddon);
}
