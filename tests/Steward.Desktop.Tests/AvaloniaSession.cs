using System.Collections.Concurrent;

using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.Hosting;

using Steward.App.Services;
using Steward.Desktop.Services;
using Steward.Desktop.Styles;

[assembly: AssemblyFixture(typeof(Steward.Desktop.Tests.AvaloniaSession))]
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace Steward.Desktop.Tests;

public sealed class AvaloniaSession : IDisposable
{
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaSession));
    private readonly string _root = Directory.CreateTempSubdirectory("steward-desktop-tests-").FullName;

    public AvaloniaSession()
    {
        ManifestIcon.Http = new HttpClient(new OfflineHandler(Requests));
        ManifestIcon.CacheFolder = Path.Combine(_root, "icons");
    }

    public ConcurrentQueue<Uri> Requests { get; } = new();

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseStewardFonts()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public IHost NewHost(AvaloniaPlatform? platform = null) =>
        App.BuildHost(platform ?? new AvaloniaPlatform(), NewFolder(), () => new OfflineHandler(Requests));

    public IHost NewHost(Func<HttpMessageHandler> handler, string? dataFolder = null) => App.BuildHost(new AvaloniaPlatform(), dataFolder ?? NewFolder(), handler);

    public string NewFolder() => Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;

    public T Run<T>(Func<T> action) => _session.Dispatch(action, TestContext.Current.CancellationToken).GetAwaiter().GetResult();

    public void Run(Action action) => _session.Dispatch(action, TestContext.Current.CancellationToken).GetAwaiter().GetResult();

    public static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Yield();
        }

        Assert.True(condition());
    }

    public static void CloseDialog(FAContentDialog dialog, Task shown)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(shown);

        dialog.Hide();
        WaitFor(() => shown.IsCompleted);
    }

    public void Dispose()
    {
        _session.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}
