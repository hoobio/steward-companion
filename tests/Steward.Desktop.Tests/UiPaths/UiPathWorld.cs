using System.Globalization;
using System.Reflection;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Steward.App.Services;
using Steward.App.ViewModels;
using Steward.Core;
using Steward.Desktop.Services;
using Steward.Desktop.Views;

namespace Steward.Desktop.Tests.UiPaths;

internal sealed class UiPathWorld : IDisposable
{
    public const string ClientVersion = "1.16.1.70000";

    private static readonly string? PngRoot = Environment.GetEnvironmentVariable("STEWARD_UIPATHS_PNG_DIR");

    private readonly string _area;
    private readonly IHost _host;
    private readonly Func<Key, bool> _keyHeld = AppDialogs.IsKeyHeld;
    private int _shot;

    public UiPathWorld(AvaloniaSession session, string area, bool signedIn = true, bool withLive = true, UiPathWorld? restartOf = null, bool apiDown = false, bool screenshot = false)
    {
        _area = area;
        Api.Down = apiDown;
        Root = restartOf?.Root ?? Path.Combine(session.NewFolder(), "World of Warcraft");
        Beta = Path.Combine(Root, "_classic_beta_");
        Live = restartOf is not null ? restartOf.Live : withLive ? Path.Combine(Root, "_forever_") : null;
        BetaAddOns = Path.Combine(Beta, "Interface", "AddOns");
        if (restartOf is null)
        {
            Flavour(Beta, "wow_classic_beta");
            if (Live is not null)
            {
                Flavour(Live, "wowf");
            }

            File.WriteAllText(Path.Combine(Root, ".build.info"), $"Version!STRING:0|Product!STRING:0\n{ClientVersion}|wowf\n{ClientVersion}|wow_classic_beta\n");
            SeedAddOns();
        }

        _host = session.NewHost(Api.Handler, restartOf?.DataFolder);
        DataFolder = _host.Services.GetRequiredService<AppDataFolder>().Path;
        Store = _host.Services.GetRequiredService<AppStateStore>();
        if (restartOf is null)
        {
            SeedState();
        }
        Store.IsReadOnly = screenshot;
        Main = _host.Services.GetRequiredService<MainViewModel>();
        if (screenshot)
        {
            ScreenshotPreview.Apply(Main);
        }

        Main.DiscoverWowInstalls = (products, overrides) => [.. new[] { Beta, Live }.OfType<string>().Select(path => WowInstalls.FromFlavourPath(path, products, overrides)!)];
        Main.ScansStore = false;
        ShellOpen.Launch = Opened.Add;
        AppDialogs.IsKeyHeld = _ => false;

        Window = _host.Services.GetRequiredService<MainWindow>();
        DefaultSize = new Size(Window.Width, Window.Height);
        Window.Width = 1280;
        Window.Height = 820;
        Window.ShowFromTray();
        Pump();
        if (signedIn)
        {
            SignIn();
        }
        else
        {
            var initialize = Main.InitializeCommand.ExecuteAsync(null);
            WaitFor(() => initialize.IsCompleted);
        }

        Pump();
    }

    public FakeApi Api { get; } = new();

    public MainWindow Window { get; }

    public Size DefaultSize { get; }

    public MainViewModel Main { get; }

    public AppStateStore Store { get; }

    public string DataFolder { get; }

    public string Root { get; }

    public string Beta { get; }

    public string? Live { get; }

    public string BetaAddOns { get; }

    public List<string> Opened { get; } = [];

    public void SignIn()
    {
        Main.IsSignedIn = true;
        Main.IsSessionChecked = true;
        var load = (Task)typeof(MainViewModel).GetMethod("LoadAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(Main, [CancellationToken.None])!;
        WaitFor(() => load.IsCompleted);
        Assert.True(load.IsCompletedSuccessfully, load.Exception?.ToString());
        AssertIsolated();
    }

    public void AssertIsolated()
    {
        var root = Path.GetFullPath(Root);
        Assert.All(Main.Installs, install => Assert.StartsWith(root, Path.GetFullPath(install.FlavourPath), StringComparison.OrdinalIgnoreCase));
    }

    public void Step(int id, string name, Action drive) => Step(UiPathLedger.Id(id), name, drive);

    public void Step(string id, string name, Action drive)
    {
        Assert.True(UiPathLedger.IsDriven(id), $"path {id} is not in the ledger as driven");
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            drive();
            Pump();
            UiPathLedger.Record(id, $"pass {started.ElapsedMilliseconds}ms");
            Shot(id, name);
        }
        catch
        {
            UiPathLedger.Record(id, $"fail {started.ElapsedMilliseconds}ms");
            Shot(id, name + "-failed");
            throw;
        }
    }

    public void Shot(string id, string name)
    {
        if (PngRoot is not { Length: > 0 })
        {
            return;
        }

        Pump();
        var directory = Directory.CreateDirectory(Path.Combine(PngRoot, _area)).FullName;
        var path = Path.Combine(directory, $"{++_shot:000}-{id}-{Slug(name)}.png");
        using var frame = Window.CaptureRenderedFrame()!;
        var popups = Roots.Skip(1).ToList();
        if (popups.Count == 0)
        {
            frame.Save(path, PngBitmapEncoderOptions.Default);
            return;
        }

        using var target = new RenderTargetBitmap(frame.PixelSize, frame.Dpi);
        using (var context = target.CreateDrawingContext())
        {
            context.DrawImage(frame, new Rect(frame.Size));
            foreach (var popup in popups)
            {
                using var image = popup.CaptureRenderedFrame();
                if (image is not null)
                {
                    context.DrawImage(image, new Rect(Window.PointToClient(popup.PointToScreen(default)), image.Size));
                }
            }
        }

        target.Save(path, PngBitmapEncoderOptions.Default);
    }

    private static string Slug(string name) => string.Concat(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');

    public static void Pump()
    {
        for (var pass = 0; pass < 5; pass++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    // RunJobs runs queued jobs only; dispatcher timers (tooltips, dialog fades, the minimise-then-hide delay) fire only inside a pushed frame.
    public static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            var frame = new DispatcherFrame();
            DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(5));
            Dispatcher.UIThread.PushFrame(frame);
        }

        Assert.True(condition());
    }

    public IEnumerable<TopLevel> Roots => [Window, .. Window.OpenedPopups.Select(popup => popup.Child is { } child ? TopLevel.GetTopLevel(child) : null).OfType<TopLevel>().Where(top => top != Window).Distinct()];

    public IEnumerable<Visual> Everything => Roots.SelectMany(root => root.GetSelfAndVisualDescendants()).Distinct();

    public T Find<T>(Func<T, bool> where)
        where T : Visual =>
        Everything.OfType<T>().Where(control => control is not Control c || c.IsEffectivelyVisible).FirstOrDefault(where)
            ?? throw new InvalidOperationException($"No visible {typeof(T).Name} matched");

    public T? TryFind<T>(Func<T, bool> where)
        where T : Visual =>
        Everything.OfType<T>().Where(control => control is not Control c || c.IsEffectivelyVisible).FirstOrDefault(where);

    public Button Named(string automationName) => Find<Button>(button => Avalonia.Automation.AutomationProperties.GetName(button) == automationName);

    public T ByName<T>(string name)
        where T : Control => Find<T>(control => control.Name == name);

    public static string Text(Visual root) => string.Join(" ", root.GetSelfAndVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Avalonia.Controls.Documents.Run>().Select(run => run.Text)) : text.Text));

    public static void Click(Control target)
    {
        Pump();
        var (root, point) = Locate(target);
        ClickAt(root, point);
    }

    public static void ClickAt(TopLevel root, Point point)
    {
        root.MouseMove(point);
        root.MouseDown(point, MouseButton.Left);
        root.MouseUp(point, MouseButton.Left);
        Pump();
    }

    public static void Hover(Control target)
    {
        Pump();
        // A zero show delay opens the tooltip on the move itself, so a hover needs no wait on the dispatcher timer.
        foreach (var control in target.GetSelfAndVisualAncestors().OfType<Control>())
        {
            ToolTip.SetShowDelay(control, 0);
        }

        var (root, point) = Locate(target);
        root.MouseMove(point);
        Pump();
    }

    public static void Wheel(Control target, double notches)
    {
        var (root, point) = Locate(target);
        root.MouseMove(point);
        root.MouseWheel(point, new Vector(0, notches));
        Pump();
    }

    public static (TopLevel Root, Point Point) Locate(Control target)
    {
        var root = TopLevel.GetTopLevel(target) ?? throw new InvalidOperationException($"{target} is not in a visual tree");
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), root)
            ?? throw new InvalidOperationException($"{target} has no position in its root");
        return (root, point);
    }

    public TopLevel FocusRoot => Roots.LastOrDefault(root => root.FocusManager?.GetFocusedElement() is not null) ?? Window;

    public IInputElement? Focused => FocusRoot.FocusManager?.GetFocusedElement();

    public void Press(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var root = FocusRoot;
        root.KeyPress(key, modifiers, PhysicalKey.None, null);
        root.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Pump();
    }

    public void Type(string text)
    {
        FocusRoot.KeyTextInput(text);
        Pump();
    }

    public FAContentDialog OpenDialog() => Find<FAContentDialog>(_ => true);

    public FAContentDialog WaitForDialog() => WaitForDialog<FAContentDialog>();

    public T WaitForDialog<T>()
        where T : FAContentDialog
    {
        T? dialog = null;
        WaitFor(() => Everything.OfType<FAContentDialog>().Where(found => found.IsEffectivelyVisible).ToList() is [T only]
            && (dialog = only).IsLoaded && Focused is Visual focused && only.IsVisualAncestorOf(focused));
        return dialog!;
    }

    public void WaitForNoDialog() => WaitFor(() => TryFind<FAContentDialog>(_ => true) is null);

    public static Button DialogButton(FAContentDialog dialog, string name) =>
        dialog.GetVisualDescendants().OfType<Button>().First(button => button.Name == name);

    public void Navigate(string automationName)
    {
        Click(ByName<Button>($"{automationName}Item"));
        WaitFor(() => Window.CurrentPage?.GetType().Name.StartsWith(automationName, StringComparison.Ordinal) == true || (automationName == "Addons" && Window.CurrentPage is HomePage));
    }

    public static string? Tip(Control control) => ToolTip.GetTip(control) switch
    {
        string text => text,
        Visual visual => Text(visual),
        var other => other?.ToString(),
    };

    public static bool IsOver(Control control) => control.Classes.Contains(":pointerover");

    public void Dispose()
    {
        foreach (var popup in Window.OpenedPopups.ToList())
        {
            popup.Close();
        }

        while (TryFind<FAContentDialog>(_ => true) is { } dialog)
        {
            dialog.Hide();
            WaitFor(() => !dialog.IsVisible || TryFind<FAContentDialog>(found => found == dialog) is null);
            Pump();
        }

        Window.Hide();
        ShellOpen.Launch = null;
        AppDialogs.IsKeyHeld = _keyHeld;

        UiPathLedger.Unrouted(_area, Api.Unrouted);
        _host.Dispose();
    }

    private static void Flavour(string path, string product)
    {
        Directory.CreateDirectory(Path.Combine(path, "Interface", "AddOns"));
        File.WriteAllText(Path.Combine(path, ".flavor.info"), $"Product Flavor!STRING:0\n{product}\n");
    }

    private void SeedAddOns()
    {
        var current = TocFile.InterfaceNumber(ClientVersion)!.Value.ToString(CultureInfo.InvariantCulture);
        Toc("Steward", "Steward", "0.0.0-d3f1d44", current);
        Toc("StewardGuides", "Steward: Guides", "0.0.0-5e1a2b3", current);
        Toc("StewardAuctioning", "Steward: Auctioning", "0.0.0-a7c9e01", current);
        Toc("StewardJourney", "Steward: Journey", "0.0.0-b2d4f68", current);
        Toc("HoobiScripts", "Hoobi Scripts", "0.0.0-ec7e7a3", current);
        Toc("RXPGuides", "RestedXP Guides", "v4.11.22", current);
        Toc("BugSack", "BugSack", "v11.2.0", current);
        Toc("!BugGrabber", "BugGrabber", "v11.0.3", current);
        Toc("Details", "Details! Damage Meter", "Details.20251004", current);
        Toc("WeakAuras", "WeakAuras", "5.19.12", current);
        var savedVariables = Directory.CreateDirectory(Path.Combine(Beta, "WTF", "Account", "1#1", "SavedVariables")).FullName;
        File.WriteAllText(Path.Combine(savedVariables, "Details.lua"), "Details = {}\n");
        File.WriteAllText(Path.Combine(savedVariables, "Steward.lua"), """
            StewardDB = {
            ["characters"] = {
            ["Player-4372-0A1B2C3D"] = {
            ["name"] = "Hoobi",
            ["realm"] = "ClassicBetaPvP2",
            ["guild"] = "Forever Guild",
            ["level"] = 60,
            ["classID"] = 1,
            ["raceID"] = 1,
            ["linkedUserId"] = "100",
            ["observedAt"] = 1760000000,
            },
            },
            }

            """);
    }

    private void Toc(string folder, string title, string version, string interfaceNumber)
    {
        var path = Directory.CreateDirectory(Path.Combine(BetaAddOns, folder)).FullName;
        File.WriteAllText(Path.Combine(path, $"{folder}.toc"), $"## Interface: {interfaceNumber}\n## Title: {title}\n## Version: {version}\n## IconTexture: Interface\\Icons\\INV_Misc_QuestionMark\n");
    }

    private void SeedState()
    {
        var installed = DateTimeOffset.Now.AddDays(-1);
        InstalledAddonRecord Record(string version, string channel, params string[] folders) => new(version, channel, null, installed, Folders: folders.Length > 0 ? folders : null);
        var installs = new Dictionary<string, InstalledAddonRecord>(StringComparer.OrdinalIgnoreCase)
        {
            [AppStateStore.Key(Beta, "steward")] = Record("0.0.0-d3f1d44", "pre-release"),
            [AppStateStore.Key(Beta, "steward-guides")] = Record("0.0.0-5e1a2b3", "pre-release"),
            [AppStateStore.Key(Beta, "steward-auctioning")] = Record("0.0.0-a7c9e01", "pre-release"),
            [AppStateStore.Key(Beta, "steward-journey")] = Record("0.0.0-b2d4f68", "pre-release"),
            [AppStateStore.Key(Beta, "hoobiscripts")] = Record("0.0.0-ec7e7a3", "pre-release"),
            [AppStateStore.Key(Beta, "restedxp")] = Record("v4.11.22", "release"),
            [AppStateStore.Key(Beta, "bugsack")] = Record("v11.2.0", "release"),
            [AppStateStore.Key(Beta, "buggrabber")] = Record("v11.0.3", "release"),
        };
        Store.Save(Store.Load() with
        {
            AutoUpdate = "never",
            GuildId = "1",
            SelectedInstall = Beta,
            Installs = installs,
            Channels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["restedxp"] = "release", ["bugsack"] = "release", ["buggrabber"] = "release" },
            ForeverSelectionMoved = true,
        });
    }
}
