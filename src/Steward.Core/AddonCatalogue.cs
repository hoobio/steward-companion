using System.Text.Json;

namespace Steward.Core;

public static class AddonCatalogue
{
    public const string StewardSource = "Steward";

    public static IReadOnlyList<ManagedAddon> Visible(
        IReadOnlyList<ManagedAddon>? server, IReadOnlyList<ManagedAddon> configured, IReadOnlySet<string> features) =>
        server ?? [.. configured.Where(addon => addon.Features.Any(features.Contains))];

    public static bool UpdatesWhileUnreachable(IReadOnlyList<ManagedAddon>? server, string addonId) =>
        server?.Any(addon => string.Equals(addon.Id, addonId, StringComparison.OrdinalIgnoreCase) && addon.Source == StewardSource) == true;

    public static bool Same(IReadOnlyList<CatalogueAddon>? left, IReadOnlyList<CatalogueAddon>? right) =>
        Json(left) == Json(right);

    private static string Json(IReadOnlyList<CatalogueAddon>? catalogue) =>
        JsonSerializer.Serialize(catalogue, CompanionJsonContext.Default.IReadOnlyListCatalogueAddon);
}
