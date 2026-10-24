using System.Reflection;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Steward.App.Services;
using Steward.App.ViewModels;
using Steward.Core;
using Steward.Desktop.Views;

namespace Steward.Desktop.Tests;

public sealed class SettingsPageTests(AvaloniaSession session)
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void Settings_page_renders_its_states() => session.Run(() =>
    {
        using var host = session.NewHost();
        var services = host.Services;
        var main = services.GetRequiredService<MainViewModel>();
        var page = services.GetRequiredService<SettingsPage>();
        var temp = session.NewFolder();
        var added = new List<WowInstallViewModel>
        {
            AddInstall(main, temp, "_classic_beta_", "wow_classic_beta", "1.60.1.70374", "World of Warcraft: Forever - Beta"),
            AddInstall(main, temp, "_forever_", "wowf", "1.60.1.70245", "World of Warcraft: Forever"),
        };
        ((HashSet<string>)typeof(MainViewModel).GetField("_features", Private)!.GetValue(main)!).Add("addons");
        main.UserName = "Hoobi";
        main.Role = "global";
        main.AvatarUri = new Uri("avares://Steward.Desktop/Assets/Steward-256.png");
        main.MinimizeToTray = true;
        main.CloseToTray = false;
        main.CurseForgeEnabled = true;
        main.AutoUpdateIndex = 1;
        main.CanStartWithWindows = false;
        main.StartWithWindows = false;
        var window = Host(page);
        var scroller = page.FindControl<ScrollViewer>("Scroller")!;

        Scroll(scroller, window, 0);
        Save(window, "settings-top");
        Assert.Contains(page.GetVisualDescendants().OfType<SettingsCard>(), card => card.Header as string == "Hoobi");
        Assert.Equal(2, page.FindControl<ItemsControl>("InstallsList")!.ItemCount);
        var segmented = page.GetVisualDescendants().OfType<ListBox>().Single();
        Assert.Equal(1, main.AutoUpdateIndex);
        Assert.Equal(1, segmented.SelectedIndex);

        main.AutoUpdateIndex = 2;
        main.CanStartWithWindows = true;
        main.StartWithWindows = true;
        main.CloseToTray = true;
        Scroll(scroller, window, 330);
        Save(window, "settings-behaviour");

        Scroll(scroller, window, double.MaxValue);
        Save(window, "settings-bottom");

        main.AppUpdateProgress = StoreUpdateProgress.From(AppUpdatePhase.Pending, 0, 0, 0);
        Save(window, "about-pending");
        main.AppUpdateProgress = StoreUpdateProgress.From(AppUpdatePhase.Downloading, 0.32, 9_800_000, 30_513_561);
        Save(window, "about-downloading");
        main.AppUpdateProgress = StoreUpdateProgress.From(AppUpdatePhase.Installing, 0.9, 0, 0);
        Save(window, "about-installing");
        Assert.True(page.GetVisualDescendants().OfType<ProgressBar>().Single().IsVisible);
        main.AppUpdateProgress = StoreUpdateProgress.None;

        page.FindControl<SettingsCard>("InstallsHeader")!.RaiseEvent(new RoutedEventArgs(SettingsCard.ClickEvent));
        Scroll(scroller, window, 0);
        Save(window, "settings-installs-collapsed");
        Assert.False(page.FindControl<ItemsControl>("InstallsList")!.IsVisible);

        Scroll(scroller, window, 0);
        window.Width = 768;
        Scroll(scroller, window, 0);
        Save(window, "settings-narrow");
        window.Width = 1280;
        Scroll(scroller, window, 0);
        var aboutCard = page.FindControl<SettingsCard>("AboutCard")!;
        var original = aboutCard.Background;
        typeof(MainViewModel).GetField("_pendingAppUpdateFocus", Private)!.SetValue(main, true);
        typeof(SettingsPage).GetMethod("ShowPendingAppUpdateFocus", Private)!.Invoke(page, null);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(5);
        Assert.NotSame(original, aboutCard.Background);
        Assert.False(main.TakeAppUpdateFocusRequest());
        Save(window, "about-focus");

        Release(window);

        foreach (var install in added)
        {
            main.Installs.Remove(install);
        }
    });

    [Fact]
    public void Group_corners_follow_the_visible_cards() => session.Run(() =>
    {
        using var host = session.NewHost();
        var page = host.Services.GetRequiredService<SettingsPage>();
        var window = Host(page);
        var group = (StackPanel)page.FindControl<SettingsCard>("MinimizeToTrayCard")!.Parent!;
        var cards = group.Children.OfType<SettingsCard>().ToList();
        cards[0].IsVisible = false;
        cards[^1].IsVisible = false;
        window.CaptureRenderedFrame();

        var visible = cards.Where(card => card.IsVisible).ToList();
        Assert.True(visible.Count >= 2);
        Assert.Equal(new CornerRadius(8, 8, 0, 0), visible[0].CornerRadius);
        Assert.Equal(new CornerRadius(0, 0, 8, 8), visible[^1].CornerRadius);
        Assert.All(visible.Skip(1).SkipLast(1), card => Assert.Equal(new CornerRadius(0), card.CornerRadius));
        for (var i = 1; i < visible.Count; i++)
        {
            Assert.Equal(1, visible[i].Bounds.Top - visible[i - 1].Bounds.Bottom, 3);
        }

        cards[0].IsVisible = true;
        window.CaptureRenderedFrame();
        Assert.Equal(new CornerRadius(8, 8, 0, 0), cards[0].CornerRadius);
        Assert.Equal(new CornerRadius(0), visible[0].CornerRadius);
        Release(window);
    });

    [Fact]
    public void Manage_installs_brings_the_installs_card_into_view() => session.Run(() =>
    {
        using var host = session.NewHost();
        var page = host.Services.GetRequiredService<SettingsPage>();
        var window = Host(page);
        window.Height = 400;
        var scroller = page.FindControl<ScrollViewer>("Scroller")!;
        Scroll(scroller, window, double.MaxValue);
        window.CaptureRenderedFrame();
        var installs = page.FindControl<Border>("InstallsGroup")!;
        Assert.True(installs.TranslatePoint(default, scroller)!.Value.Y < 0);

        page.ShowInstalls();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        var top = installs.TranslatePoint(default, scroller)!.Value.Y;
        Assert.InRange(top, 0, scroller.Viewport.Height - installs.Bounds.Height);
        Release(window);
    });

    [Fact]
    public void Turning_CurseForge_back_on_probes_the_catalogue_CurseForge_rows() => session.Run(() =>
    {
        using var host = session.NewHost();
        var main = host.Services.GetRequiredService<MainViewModel>();
        ((HashSet<string>)typeof(MainViewModel).GetField("_features", Private)!.GetValue(main)!).Add("addons");
        const string manifestBase = "https://example.invalid/curseforge-toggle/6273/88568/";
        typeof(MainViewModel).GetMethod("SetAddonCatalogue", Private)!.Invoke(main, [new List<CatalogueAddon> { new("bugsack", "BugSack", manifestBase, "BugSack", "CurseForge") }]);
        var install = AddInstall(main, session.NewFolder(), "_classic_beta_", "wow_classic_beta", "1.60.1.70374", "World of Warcraft: Forever - Beta");

        main.CurseForgeEnabled = false;
        session.Requests.Clear();
        main.CurseForgeEnabled = true;

        AvaloniaSession.WaitFor(() => session.Requests.Any(uri => uri.AbsoluteUri.StartsWith(manifestBase, StringComparison.Ordinal)));
        main.Installs.Remove(install);
    });

    private static void Release(Window window)
    {
        ((Border)((Grid)window.Content!).Children[2]).Child = null;
        window.Close();
    }

    private static Window Host(SettingsPage page)
    {
        var resources = Application.Current!;
        var window = new Window
        {
            Width = 1280,
            Height = 820,
            Background = (IBrush)resources.FindResource("WindowGroundBrush")!,
            Content = new Grid
            {
                Children =
                {
                    new Border { Background = (IBrush)resources.FindResource("GroundGlowTopLeft")! },
                    new Border { Background = (IBrush)resources.FindResource("GroundGlowTopRight")! },
                    new Border { Margin = new Thickness(48, 48, 0, 0), Child = page },
                },
            },
        };
        window.Show();
        return window;
    }

    private static WowInstallViewModel AddInstall(MainViewModel main, string temp, string flavour, string product, string client, string name)
    {
        var path = Path.Combine(temp, flavour);
        Directory.CreateDirectory(Path.Combine(path, "Interface", "AddOns"));
        var install = new WowInstall(temp, flavour, path, Path.Combine(path, "Interface", "AddOns"), product, client, name);
        return (WowInstallViewModel)typeof(MainViewModel)
            .GetMethod("AddInstall", Private)!
            .Invoke(main, [install, false])!;
    }

    private static void Scroll(ScrollViewer scroller, Window window, double offset)
    {
        window.CaptureRenderedFrame();
        scroller.Offset = new Vector(0, Math.Min(offset, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height)));
    }

    private static void Save(Window window, string name)
    {
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("STEWARD_SETTINGS_PNG_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            frame.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
        }

        TextCentring.Verify(window, name);
    }
}
