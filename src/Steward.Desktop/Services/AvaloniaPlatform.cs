using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using Steward.App.Services;
using Steward.Core;

namespace Steward.Desktop.Services;

public sealed class AvaloniaPlatform : IUiPlatform
{
    private readonly Dictionary<(Uri Uri, int? Width), PlatformImage> _uriImages = [];

    public HttpClient? Http { get; set; }

    public TopLevel? Owner { get; set; }

    public nint OwnerWindowHandle => Owner?.TryGetPlatformHandle()?.Handle ?? 0;

    public bool HasThreadAccess => Dispatcher.UIThread.CheckAccess();

    public bool TryEnqueue(Action action)
    {
        Dispatcher.UIThread.Post(action);
        return true;
    }

    public UiTimer CreateTimer() => new Timer();

    public object ImageFromUri(Uri uri, int? decodeWidth)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (uri.Scheme is "avares")
        {
            using var stream = AssetLoader.Open(uri);
            return Decode(stream, decodeWidth);
        }

        if (_uriImages.TryGetValue((uri, decodeWidth), out var cached))
        {
            return cached;
        }

        var size = decodeWidth ?? 0;
        var image = new PlatformImage { Drawing = new ImageDrawing { Rect = new Rect(0, 0, size, size) } };
        _uriImages[(uri, decodeWidth)] = image;
        _ = LoadAsync(uri, decodeWidth, image);
        return image;
    }

    public object ImageFromAsset(string assetPath, int? decodeWidth) => ImageFromUri(AssetUri(assetPath), decodeWidth);

    public bool HasAsset(string assetPath) => AssetLoader.Exists(AssetUri(assetPath));

    public object ImageFromBytes(byte[] encoded, int decodeWidth, Action onFailed)
    {
        try
        {
            using var stream = new MemoryStream(encoded);
            return Decode(stream, decodeWidth);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or NotSupportedException)
        {
            Dispatcher.UIThread.Post(onFailed);
            var failed = new PlatformImage();
            failed.MarkFailed();
            return failed;
        }
    }

    public Task<object> ImageFromPremultipliedBgraAsync(DecodedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            for (var row = 0; row < image.Height; row++)
            {
                Marshal.Copy(image.Bgra, row * image.Width * 4, buffer.Address + (row * buffer.RowBytes), image.Width * 4);
            }
        }

        return Task.FromResult<object>(bitmap);
    }

    public Task<DecodedImage> DecodeBgraAsync(byte[] encoded, int? squareSize) => Task.Run(() =>
    {
        using var stream = new MemoryStream(encoded);
        using var decoded = Decode(stream, null);
        using var scaled = squareSize is { } size ? decoded.CreateScaledBitmap(new PixelSize(size, size), BitmapInterpolationMode.HighQuality) : null;
        var source = scaled ?? decoded;
        var width = source.PixelSize.Width;
        var height = source.PixelSize.Height;
        var pixels = new byte[width * height * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            source.CopyPixels(new PixelRect(source.PixelSize), handle.AddrOfPinnedObject(), pixels.Length, width * 4);
        }
        finally
        {
            handle.Free();
        }

        var rgba = source.Format == PixelFormat.Rgba8888;
        var premultiplied = source.AlphaFormat == AlphaFormat.Premul;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (rgba)
            {
                (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
            }

            var alpha = pixels[i + 3];
            if (premultiplied && alpha is > 0 and < 255)
            {
                pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / alpha);
                pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / alpha);
                pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / alpha);
            }
        }

        return new DecodedImage(width, height, pixels);
    });

    public void SetClipboardText(string text) => _ = Owner?.Clipboard?.SetTextAsync(text);

    public async Task<string?> PickFolderAsync()
    {
        if (Owner?.StorageProvider is not { CanPickFolder: true } storage)
        {
            return null;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false, Title = OperatingSystem.IsWindows() ? null : "Choose the World of Warcraft folder" }).ConfigureAwait(true);
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private static Uri AssetUri(string assetPath) => new($"avares://Steward.Desktop/Assets/{assetPath}");

    private static Bitmap Decode(Stream stream, int? decodeWidth)
    {
        try
        {
            return decodeWidth is { } width ? Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality) : new Bitmap(stream);
        }
        catch (NullReferenceException ex)
        {
            // Avalonia.Skia 12.1.3 throws NullReferenceException when no codec recognises the bytes.
            throw new ArgumentException("The bytes are not an image Skia can decode.", nameof(stream), ex);
        }
    }

    private async Task LoadAsync(Uri uri, int? decodeWidth, PlatformImage image)
    {
        if (Http is not { } http)
        {
            return;
        }

        try
        {
            var bytes = await http.GetByteArrayAsync(uri).ConfigureAwait(false);
            var scaling = await Dispatcher.UIThread.InvokeAsync(() => Owner?.RenderScaling ?? 1);
            var bitmap = await Task.Run(() =>
            {
                using var stream = new MemoryStream(bytes);
                return Decode(stream, decodeWidth is { } width ? (int)Math.Ceiling(width * scaling) : null);
            }).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var width = decodeWidth ?? bitmap.Size.Width;
                image.Drawing = new ImageDrawing { ImageSource = bitmap, Rect = new Rect(0, 0, width, width * bitmap.Size.Height / bitmap.Size.Width) };
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException or InvalidOperationException or IOException or NotSupportedException)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _uriImages.Remove((uri, decodeWidth));
                image.MarkFailed();
            });
        }
    }

    private sealed class Timer : UiTimer
    {
        private readonly DispatcherTimer _timer = new();

        public Timer() => _timer.Tick += (_, _) =>
        {
            if (!IsRepeating)
            {
                _timer.Stop();
            }

            RaiseTick();
        };

        public override TimeSpan Interval { get => _timer.Interval; set => _timer.Interval = value; }

        public override bool IsRepeating { get; set; } = true;

        public override bool IsRunning => _timer.IsEnabled;

        protected override void OnStart() => _timer.Start();

        protected override void OnStop() => _timer.Stop();
    }
}
