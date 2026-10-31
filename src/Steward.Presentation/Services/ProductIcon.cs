using System.Collections.Concurrent;

namespace Steward.App.Services;

public static class ProductIcon
{
    private static readonly ConcurrentDictionary<string, object> Cache = new();

    public static IReadOnlyDictionary<string, string> Urls { get; set; } = new Dictionary<string, string>();

    public static object? For(IUiPlatform platform, string? productCode)
    {
        ArgumentNullException.ThrowIfNull(platform);

        return productCode is not null && Urls.TryGetValue(productCode, out var url) && Bundled(platform, productCode) is not null
            ? platform.ImageFromUri(new Uri(url), 32)
            : Bundled(platform, productCode);
    }

    public static object? Bundled(IUiPlatform platform, string? productCode)
    {
        ArgumentNullException.ThrowIfNull(platform);

        if (productCode is null)
        {
            return null;
        }

        var file = Path.Combine(AppContext.BaseDirectory, "Assets", "Products", $"{productCode}.png");
        if (!AppInfo.IsPackaged && File.Exists(file))
        {
            return Cache.GetOrAdd(productCode, _ => platform.ImageFromUri(new Uri(file), 32));
        }

        var asset = $"Products/{productCode}.png";
        return platform.HasAsset(asset) ? Cache.GetOrAdd(productCode, _ => platform.ImageFromAsset(asset, 32)) : null;
    }
}
