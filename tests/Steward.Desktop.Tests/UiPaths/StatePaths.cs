using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;

using Steward.App.Services;
using Steward.App.ViewModels;
using Steward.Core;
using Steward.Desktop.Views;

using static Steward.Desktop.Tests.UiPaths.UiPathWorld;

namespace Steward.Desktop.Tests.UiPaths;

public sealed class StatePaths(AvaloniaSession session)
{
    private const ulong UpdateSize = 30_513_561;

    [Fact]
    public void P110_P112_previews() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "previews");

        world.Step(110, "banners preview", () =>
        {
            BannersPreview.Apply(world.Main);
            Pump();
            var titles = world.Main.Banners.Select(banner => banner.Title).Where(title => !title.StartsWith("Copy your addons", StringComparison.Ordinal)).ToList();
            Assert.Equal(["Update required", "Addon manifest slow to respond", "Sync is healthy", "New in this release"], titles);
            var page = world.Window.CurrentPage!;
            Assert.Equal(3, page.GetVisualDescendants().OfType<Button>().Count(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == "Dismiss"));
            world.Shot("110", "four-banners");
            Click(page.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible && button.Content as string == "Details"));
            Assert.Equal("https://github.com/hoobio/steward-companion/releases", Assert.Single(world.Opened));
            var info = page.GetVisualDescendants().OfType<Border>().First(border => border.DataContext is BannerViewModel { Title: "New in this release" });
            Click(info.GetVisualDescendants().OfType<Button>().First(button => AutomationProperties.GetName(button) == "Dismiss"));
            WaitFor(() => world.Main.Banners.All(banner => banner.Title != "New in this release"));
            Assert.Equal(4, world.Main.Banners.Count);
        });

        world.Step(111, "update progress in the InfoBar and the About card", () =>
        {
            world.Main.AppUpdate = new AddonRelease(string.Empty, string.Empty, string.Empty, 0, DateTimeOffset.Now);
            world.Main.ReportStoreProgress(StoreUpdateProgress.From(AppUpdatePhase.Downloading, StoreUpdateProgress.DownloadShare * 0.42, (ulong)(UpdateSize * 0.42), UpdateSize));
            WaitFor(() => world.Main.AppUpdateProgress.Text.StartsWith("Downloading update", StringComparison.Ordinal));
            WaitFor(() => Text(world.Window).Contains("Downloading update", StringComparison.Ordinal));
            Assert.Contains("42%", world.Main.AppUpdateProgress.Text, StringComparison.Ordinal);
            world.Shot("111", "infobar");
            world.Navigate("Settings");
            Assert.Contains("Downloading update", world.ByName<SettingsCard>("AboutCard").Description as string, StringComparison.Ordinal);
            world.Navigate("Addons");
        });

        world.Step(112, "client outdated error bar", () =>
        {
            world.Main.ReportClientOutdated("This version of Steward no longer works with the guild API. Update to 0.13.0 or newer.");
            WaitFor(() => world.TryFind<Button>(button => button.Content as string == "Update Steward") is not null);
            Assert.Contains("no longer works with the guild API", Text(world.Window), StringComparison.Ordinal);
        });
    });

    [Fact]
    public void P113_forever_install_banner() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "previews-forever", withLive: false);

        world.Step(113, "Install World of Warcraft: Forever banner", () =>
            Assert.Contains("Install World of Warcraft: Forever", Text(world.Window.CurrentPage!), StringComparison.Ordinal));
    });

    [Fact]
    public void P108_P109_curseforge_link() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "curseforge-link");
        Task? received = null;

        world.Step(108, "a curseforge link asks to install DBM", () =>
        {
            received = world.Main.ReceiveCurseForgeLinkAsync("curseforge://install?addonId=3358&fileId=9181264");
            var dialog = world.WaitForDialog();
            var text = Text(dialog);
            Assert.Contains("Install DBM - Deadly Boss Mods (DBM-Core)?", text, StringComparison.Ordinal);
            Assert.Contains("12.1.14", text, StringComparison.Ordinal);
            Assert.Equal("CloseButton", (world.Focused as Control)?.Name);
        });

        world.Step(109, "Cancel installs nothing", () =>
        {
            Click(DialogButton(world.OpenDialog(), "CloseButton"));
            world.WaitForNoDialog();
            WaitFor(() => received!.IsCompleted);
            Assert.DoesNotContain(Directory.EnumerateDirectories(world.BetaAddOns), folder => Path.GetFileName(folder).StartsWith("DBM", StringComparison.Ordinal));
            Assert.DoesNotContain(world.Api.Requests, request => request.Contains(".zip", StringComparison.Ordinal));
        });
    });

    [Fact]
    public void P138_started_with_the_api_unreachable() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "api-unreachable", apiDown: true);

        world.Step(138, "started with the API unreachable", () =>
        {
            WaitFor(() => world.Main.StatusMessage == "Steward APIs are temporarily unavailable.");
            Assert.NotNull(world.TryFind<Button>(button => button.Content as string == "Retry"));
            var tip = Tip(world.ByName<Button>("AccountButton"));
            Assert.False(string.IsNullOrEmpty(tip));
            Assert.False(tip!.StartsWith(',') || tip.StartsWith(' '), tip);
        });
    });

    [Fact]
    public void P146_signed_out_gate() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "gate", signedIn: false);

        world.Step(146, "signed-out gate", () =>
        {
            Assert.False(world.Main.IsSignedIn);
            var text = Text(world.Window);
            Assert.Contains("Sign in with Discord", text, StringComparison.Ordinal);
            Assert.Contains("Help", text, StringComparison.Ordinal);
            Assert.Contains($"Steward {MainViewModel.InstalledVersion}", text, StringComparison.Ordinal);
            Assert.Null(world.TryFind<Button>(button => button.Name == "AccountButton"));
        });
    });
}
