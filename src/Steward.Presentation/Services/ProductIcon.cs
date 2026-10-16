using System.Collections.Concurrent;

namespace Steward.App.Services;

public static class ProductIcon
{
    private static readonly ConcurrentDictionary<string, object> Cache = new();

    public static object? For(IUiPlatform platform, string? productCode)
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
