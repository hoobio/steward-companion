using System.Net;
using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

using Steward.Desktop.Services;

namespace Steward.Desktop.Tests;

public sealed class PlatformTests(AvaloniaSession session)
{
    [Fact]
    public void Uri_image_draws_once_its_download_decodes() => session.Run(() =>
    {
        using var asset = AssetLoader.Open(new Uri("avares://Steward.Desktop/Assets/Steward-256.png"));
        using var copy = new MemoryStream();
        asset.CopyTo(copy);
        var gate = new TaskCompletionSource();
        using var http = new HttpClient(new StubHandler(copy.ToArray(), HttpStatusCode.OK, gate.Task));
        var platform = new AvaloniaPlatform { Http = http };
        var image = new Image { Source = (IImage)platform.ImageFromUri(new Uri("https://icons.test/steward.png"), 32), Width = 32, Height = 32 };
        var window = new Window { Width = 32, Height = 32, Content = image, Background = Brushes.Transparent };
        window.Show();

        var before = Centre(window.CaptureRenderedFrame()!);
        gate.SetResult();
        var drawing = (DrawingImage)image.Source!;
        AvaloniaSession.WaitFor(() => ((ImageDrawing)drawing.Drawing!).ImageSource is not null);

        var frame = window.CaptureRenderedFrame()!;
        window.Close();
        Assert.NotEqual(before, Centre(frame));
    });

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "")]
    [InlineData(HttpStatusCode.OK, "<!doctype html><html><body>index</body></html>")]
    public void Uri_image_that_cannot_load_reports_failure_to_its_icon_image(HttpStatusCode status, string body) => session.Run(() =>
    {
        using var http = new HttpClient(new StubHandler(System.Text.Encoding.UTF8.GetBytes(body), status));
        var platform = new AvaloniaPlatform { Http = http };
        var image = new Steward.Desktop.Views.IconImage { Width = 32, Height = 32 };
        var failed = false;
        image.Failed += (_, _) => failed = true;
        image.Source = (IImage)platform.ImageFromUri(new Uri($"https://icons.test/{(int)status}.png"), 32);

        AvaloniaSession.WaitFor(() => failed);
    });

    [Fact]
    public void Undecodable_bytes_report_failure_once_bound() => session.Run(() =>
    {
        var platform = new AvaloniaPlatform();
        var evicted = false;
        var image = new Steward.Desktop.Views.IconImage();
        var failed = false;
        image.Failed += (_, _) => failed = true;
        image.Source = (IImage)platform.ImageFromBytes([1, 2, 3], 32, () => evicted = true);

        AvaloniaSession.WaitFor(() => failed && evicted);
    });

    [Fact]
    public void Product_icons_load_from_the_bundled_assets() => session.Run(() =>
    {
        var platform = new AvaloniaPlatform();

        Assert.IsAssignableFrom<IImage>(Steward.App.Services.ProductIcon.For(platform, "wowf"));
        Assert.Null(Steward.App.Services.ProductIcon.For(platform, "no_such_product"));
    });

    private static uint Centre(Avalonia.Media.Imaging.WriteableBitmap frame)
    {
        var pixels = new byte[frame.PixelSize.Width * frame.PixelSize.Height * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect(frame.PixelSize), handle.AddrOfPinnedObject(), pixels.Length, frame.PixelSize.Width * 4);
        }
        finally
        {
            handle.Free();
        }

        return BitConverter.ToUInt32(pixels, ((frame.PixelSize.Height / 2 * frame.PixelSize.Width) + (frame.PixelSize.Width / 2)) * 4);
    }

    private sealed class StubHandler(byte[] body, HttpStatusCode status = HttpStatusCode.OK, Task? gate = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await (gate ?? Task.CompletedTask).ConfigureAwait(false);
            return new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
        }
    }
}
