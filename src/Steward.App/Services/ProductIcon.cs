using System.Collections.Concurrent;

using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Steward.App.Services;

public static class ProductIcon
{
    private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new();

    public static ImageSource? For(string? productCode)
    {
        if (productCode is null)
        {
            return null;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Products", $"{productCode}.png");
        if (!File.Exists(path))
        {
            return null;
        }

        return Cache.GetOrAdd(productCode, _ => new BitmapImage(new Uri(App.IsPackaged ? $"ms-appx:///Assets/Products/{productCode}.png" : path)) { DecodePixelType = DecodePixelType.Logical, DecodePixelWidth = 32 });
    }
}
