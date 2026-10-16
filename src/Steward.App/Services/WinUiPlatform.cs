using System.Runtime.InteropServices.WindowsRuntime;

using Steward.Core;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Pickers;

namespace Steward.App.Services;

public sealed class WinUiPlatform(DispatcherQueue dispatcher) : IUiPlatform
{
    public nint OwnerWindowHandle { get; set; }

    public bool HasThreadAccess => dispatcher.HasThreadAccess;

    public bool TryEnqueue(Action action) => dispatcher.TryEnqueue(() => action());

    public UiTimer CreateTimer() => new Timer(dispatcher.CreateTimer());

    public object ImageFromUri(Uri uri, int? decodeWidth) => decodeWidth is { } width
        ? new BitmapImage(uri) { DecodePixelType = DecodePixelType.Logical, DecodePixelWidth = width }
        : new BitmapImage(uri);

    public object ImageFromAsset(string assetPath, int? decodeWidth) => ImageFromUri(new Uri($"ms-appx:///Assets/{assetPath}"), decodeWidth);

    public bool HasAsset(string assetPath) => File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", assetPath));

    public object ImageFromBytes(byte[] encoded, int decodeWidth, Action onFailed)
    {
        var bitmap = new BitmapImage { DecodePixelType = DecodePixelType.Logical, DecodePixelWidth = decodeWidth };
        bitmap.ImageFailed += (_, _) => onFailed();
        using var stream = new MemoryStream(encoded);
        bitmap.SetSource(stream.AsRandomAccessStream());
        return bitmap;
    }

    public async Task<object> ImageFromPremultipliedBgraAsync(DecodedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var bitmap = new WriteableBitmap(image.Width, image.Height);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            await stream.WriteAsync(image.Bgra).ConfigureAwait(true);
        }

        bitmap.Invalidate();
        return bitmap;
    }

    public async Task<DecodedImage> DecodeBgraAsync(byte[] encoded, int? squareSize)
    {
        using var stream = new MemoryStream(encoded).AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var transform = squareSize is { } size
            ? new BitmapTransform { ScaledWidth = (uint)size, ScaledHeight = (uint)size, InterpolationMode = BitmapInterpolationMode.Fant }
            : new BitmapTransform();
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        return squareSize is { } scaled
            ? new DecodedImage(scaled, scaled, pixels.DetachPixelData())
            : new DecodedImage((int)decoder.PixelWidth, (int)decoder.PixelHeight, pixels.DetachPixelData());
    }

    public void SetClipboardText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, OwnerWindowHandle);
        picker.FileTypeFilter.Add("*");
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    private sealed class Timer : UiTimer
    {
        private readonly DispatcherQueueTimer _timer;

        public Timer(DispatcherQueueTimer timer)
        {
            _timer = timer;
            _timer.Tick += (_, _) => RaiseTick();
        }

        public override TimeSpan Interval { get => _timer.Interval; set => _timer.Interval = value; }

        public override bool IsRepeating { get => _timer.IsRepeating; set => _timer.IsRepeating = value; }

        public override bool IsRunning => _timer.IsRunning;

        protected override void OnStart() => _timer.Start();

        protected override void OnStop() => _timer.Stop();
    }
}
