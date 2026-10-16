using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Reactive;

using Steward.Desktop.Services;

namespace Steward.Desktop.Styles;

public enum AcrylicMode
{
    Blur,
    Mock,
    Fallback,
}

public static class Acrylic
{
    public const double TintOpacity = 0.15;
    public const double LuminosityOpacity = 0.96;
    public const double NoiseOpacity = 0.02;
    public const int NoiseSize = 256;

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TemplatedControl, bool>("IsEnabled", typeof(Acrylic));

    public static readonly AttachedProperty<AcrylicMode> ModeProperty =
        AvaloniaProperty.RegisterAttached<TemplatedControl, AcrylicMode>("Mode", typeof(Acrylic));

    private static readonly ConditionalWeakTable<TemplatedControl, IDisposable> Subscriptions = [];
    private static readonly Dictionary<(Color Tint, Color Fallback, double Scale), Bitmap> MockTiles = [];
    private static readonly WindowTransparencyLevel[] BlurLevels = [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur];
    private static readonly WindowTransparencyLevel[] Hint = [.. BlurLevels, WindowTransparencyLevel.Transparent];

    static Acrylic()
    {
        IsEnabledProperty.Changed.AddClassHandler<TemplatedControl>((presenter, e) =>
        {
            presenter.AttachedToVisualTree -= OnAttached;
            presenter.DetachedFromVisualTree -= OnDetached;
            if (e.GetNewValue<bool>())
            {
                presenter.AttachedToVisualTree += OnAttached;
                presenter.DetachedFromVisualTree += OnDetached;
                if (TopLevel.GetTopLevel(presenter) is not null)
                {
                    Attach(presenter);
                }
            }
        });
    }

    public static bool GetIsEnabled(TemplatedControl presenter) => presenter.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(TemplatedControl presenter, bool value) => presenter.SetValue(IsEnabledProperty, value);

    public static AcrylicMode GetMode(TemplatedControl presenter) => presenter.GetValue(ModeProperty);

    public static Color Composite(Color tint, Color? under)
    {
        double r = 0, g = 0, b = 0, a = 0;
        if (under is { } ground)
        {
            (r, g, b, a) = (ground.R / 255d, ground.G / 255d, ground.B / 255d, 1);
        }

        foreach (var opacity in new[] { LuminosityOpacity, TintOpacity })
        {
            r = tint.R / 255d * opacity + r * (1 - opacity);
            g = tint.G / 255d * opacity + g * (1 - opacity);
            b = tint.B / 255d * opacity + b * (1 - opacity);
            a = opacity + a * (1 - opacity);
        }

        return Color.FromArgb(Byte(a), Byte(r / a), Byte(g / a), Byte(b / a));
    }

    // WinUI's NoiseAsset_256x256.png holds six greys, 0 to 255 in steps of 51, binomially spread; this is the same distribution from a fixed seed, offset to zero mean so it textures without lightening.
    public static byte[] NoiseLevels()
    {
        var random = new Random(256);
        var levels = new byte[NoiseSize * NoiseSize];
        for (var i = 0; i < levels.Length; i++)
        {
            var heads = 0;
            for (var flip = 0; flip < 5; flip++)
            {
                heads += random.Next(2);
            }

            levels[i] = (byte)(heads * 51);
        }

        return levels;
    }

    public static Bitmap MockTile(Color tint, Color fallback, double scale)
    {
        if (MockTiles.TryGetValue((tint, fallback, scale), out var cached))
        {
            return cached;
        }

        var surface = Composite(tint, fallback);
        var bitmap = new WriteableBitmap(new PixelSize(NoiseSize, NoiseSize), new Vector(96 * scale, 96 * scale), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            var levels = NoiseLevels();
            var pixels = new byte[levels.Length * 4];
            for (var i = 0; i < levels.Length; i++)
            {
                var shift = NoiseOpacity * (levels[i] - 127.5);
                pixels[i * 4] = Byte((surface.B + shift) / 255);
                pixels[i * 4 + 1] = Byte((surface.G + shift) / 255);
                pixels[i * 4 + 2] = Byte((surface.R + shift) / 255);
                pixels[i * 4 + 3] = 255;
            }

            for (var row = 0; row < NoiseSize; row++)
            {
                System.Runtime.InteropServices.Marshal.Copy(pixels, row * NoiseSize * 4, buffer.Address + row * buffer.RowBytes, NoiseSize * 4);
            }
        }

        MockTiles[(tint, fallback, scale)] = bitmap;
        return bitmap;
    }

    private static byte Byte(double unit) => (byte)Math.Clamp(Math.Round(unit * 255), 0, 255);

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) => Attach((TemplatedControl)sender!);

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var presenter = (TemplatedControl)sender!;
        if (Subscriptions.TryGetValue(presenter, out var subscription))
        {
            subscription.Dispose();
            Subscriptions.Remove(presenter);
        }
    }

    private static void Attach(TemplatedControl presenter)
    {
        OnDetached(presenter, null!);
        if (TopLevel.GetTopLevel(presenter) is not { } top)
        {
            return;
        }

        var blur = RequestBlur(top);
        var owner = Owner(top);
        var region = blur && top is PopupRoot root ? new PopupRegion(root, presenter) : null;
        void Update() => Apply(presenter, top, blur, owner?.IsActive != false);

        var activation = owner?.GetObservable(WindowBase.IsActiveProperty).Subscribe(new AnonymousObserver<bool>(_ => Update()));
        Update();
        Subscriptions.Add(presenter, new Subscription(activation, region));
    }

    private static bool RequestBlur(TopLevel top)
    {
        if (top is not PopupRoot)
        {
            return false;
        }

        // A macOS popup cannot be clipped, so its blur showed untinted at the corners (16 Oct 2026); it is already Transparent, and Avalonia.Native 12.1.3 sets Opaque when hinted the level it has.
        if (!OperatingSystem.IsMacOS())
        {
            top.TransparencyLevelHint = Hint;
        }

        return BlurLevels.Contains(top.ActualTransparencyLevel);
    }

    private static WindowBase? Owner(TopLevel top)
    {
        var current = top;
        while (current is PopupRoot popup && popup.ParentTopLevel is { } parent)
        {
            current = parent;
        }

        return current as WindowBase;
    }

    private static void Apply(TemplatedControl presenter, TopLevel top, bool blur, bool active)
    {
        var tint = Resource(presenter, "FlyoutAcrylicTintColor");
        var fallback = Resource(presenter, "FlyoutAcrylicFallbackColor");
        var mode = !active ? AcrylicMode.Fallback : blur ? AcrylicMode.Blur : AcrylicMode.Mock;
        presenter.SetValue(ModeProperty, mode);
        presenter.Background = mode switch
        {
            AcrylicMode.Blur => new SolidColorBrush(Composite(tint, null)),
            AcrylicMode.Mock => Tile(MockTile(tint, fallback, top.RenderScaling)),
            _ => new SolidColorBrush(fallback),
        };
    }

    private static ImageBrush Tile(Bitmap bitmap) => new(bitmap)
    {
        TileMode = TileMode.Tile,
        Stretch = Stretch.None,
        AlignmentX = AlignmentX.Left,
        AlignmentY = AlignmentY.Top,
        DestinationRect = new RelativeRect(0, 0, bitmap.Size.Width, bitmap.Size.Height, RelativeUnit.Absolute),
    };

    private static Color Resource(TemplatedControl presenter, string key) =>
        presenter.TryFindResource(key, presenter.ActualThemeVariant, out var value) && value is Color color ? color : Colors.Transparent;

    private sealed class Subscription(IDisposable? activation, PopupRegion? region) : IDisposable
    {
        public void Dispose()
        {
            activation?.Dispose();
            region?.Dispose();
        }
    }

    // The blur is a composition visual under the whole popup window, so the window is clipped to the presenter, or the presenter's offset and corners show untinted blur.
    private sealed class PopupRegion : IDisposable
    {
        private readonly PopupRoot _root;
        private readonly TemplatedControl _presenter;
        private PixelRect _applied;

        public PopupRegion(PopupRoot root, TemplatedControl presenter)
        {
            _root = root;
            _presenter = presenter;
            _presenter.LayoutUpdated += OnLayoutUpdated;
            OnLayoutUpdated(null, EventArgs.Empty);
        }

        public void Dispose() => _presenter.LayoutUpdated -= OnLayoutUpdated;

        private void OnLayoutUpdated(object? sender, EventArgs e)
        {
            if (_root.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } handle
                || _presenter.TranslatePoint(default, _root) is not { } origin)
            {
                return;
            }

            var scale = _root.RenderScaling;
            var bounds = PixelRect.FromRect(new Rect(origin, _presenter.Bounds.Size), scale);
            if (bounds == _applied)
            {
                return;
            }

            _applied = bounds;
            Native.ClipWindow(handle.Handle, bounds.X, bounds.Y, bounds.Right, bounds.Bottom, (int)Math.Round(2 * Math.Max(0, _presenter.CornerRadius.TopLeft * scale - 2)));
        }
    }
}
