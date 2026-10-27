using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Steward.App.Services;
using Steward.App.ViewModels;
using Steward.Core;
using Steward.Desktop.Services;
using Steward.Desktop.Views;

namespace Steward.Desktop.Tests;

public sealed class ShellTests(AvaloniaSession session)
{
    [Fact]
    public void Shell_constructs_on_the_addons_page() => session.Run(() =>
    {
        using var host = session.NewHost();
        var window = host.Services.GetRequiredService<MainWindow>();

        Assert.IsType<HomePage>(window.CurrentPage);
    });

    [Fact]
    public void Main_window_carries_the_steward_icon() => session.Run(() =>
    {
        using var host = session.NewHost();
        var window = host.Services.GetRequiredService<MainWindow>();

        Assert.NotNull(window.Icon);
    });

    [Theory]
    [InlineData(typeof(HomePage))]
    [InlineData(typeof(SyncPage))]
    [InlineData(typeof(GuidesPage))]
    [InlineData(typeof(AddonSettingsPage))]
    [InlineData(typeof(SettingsPage))]
    public void Every_page_resolves_as_a_singleton(Type page) => session.Run(() =>
    {
        using var host = session.NewHost();
        var first = host.Services.GetRequiredService(page);

        Assert.Same(first, host.Services.GetRequiredService(page));
    });

    [Fact]
    public void Every_dialog_constructs() => session.Run(() =>
    {
        using var host = session.NewHost();
        var services = host.Services;
        var main = services.GetRequiredService<MainViewModel>();
        var platform = services.GetRequiredService<AvaloniaPlatform>();
        var guild = new GuildOptionViewModel(new AdminGuild("1", "Forever Guild", null, 12, null), "Officer", platform);

        Assert.All(
            new Control[]
            {
                new AddonFoldersDialog("Folders", [], platform, NullLogger.Instance),
                new ChangelogDialog("Changes", []),
                new ChooseGuildDialog([guild], guild),
                new GetAddonsDialog(null!),
                new RestedXpSignInDialog(main.RestedXp),
                new BannerList(),
            },
            Assert.NotNull);
    });

    [Fact]
    public void Choosing_an_install_picker_row_selects_that_install() => session.Run(() =>
    {
        using var host = session.NewHost();
        var window = host.Services.GetRequiredService<MainWindow>();
        LiveUpdatesPreview.Apply(window.ViewModel);
        var folder = session.NewFolder();
        var alpha = Install(host.Services, Path.Combine(folder, "a", "_forever_"), "Alpha");
        var beta = Install(host.Services, Path.Combine(folder, "b", "_forever_"), "Beta");
        window.ViewModel.Installs.Add(alpha);
        window.ViewModel.Installs.Add(beta);
        window.ViewModel.SelectInstall(alpha);
        window.Show();
        var picker = window.FindControl<Button>("InstallPicker")!;
        picker.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        picker.Flyout!.ShowAt(picker);
        Dispatcher.UIThread.RunJobs();
        var row = window.OpenedPopups.SelectMany(popup => popup.Child!.GetVisualDescendants()).OfType<Button>()
            .First(button => button.Classes.Contains("Menu") && button.Tag == beta);

        row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Same(beta, window.ViewModel.SelectedInstall);
        Assert.False(picker.Flyout.IsOpen);
        window.Hide();
    });

    private static WowInstallViewModel Install(IServiceProvider services, string path, string label)
    {
        var install = new WowInstall(Path.GetDirectoryName(path)!, "_forever_", path, Path.Combine(path, "Interface", "AddOns"), "wowf", "1.0.0.1", "World of Warcraft: Forever");
        return new WowInstallViewModel(
            install, "Forever", label, [], (_, _) => [], (_, _) => Task.FromResult<IReadOnlyList<ProviderAddonRecord>>([]), (_, _) => false, (_, _) => { },
            _ => Task.FromResult(true), services.GetRequiredService<AddonUpdater>(), services.GetRequiredService<AppStateStore>(), (_, _) => Task.FromResult(true),
            () => false, _ => { }, (_, _, _) => Task.FromResult(true), _ => { }, _ => { }, (_, _) => Task.CompletedTask, (_, _, _, _, _, _) => Task.CompletedTask,
            () => null, services.GetRequiredService<AvaloniaPlatform>(), NullLogger.Instance);
    }

    [Fact]
    public void Avalonia_X11_still_offers_the_speculative_activation_the_show_check_applies() =>
        Assert.NotNull(X11ActiveWindow.SetActiveSpeculatively);

    [Fact]
    public void Sign_in_gate_links_are_not_underlined() => session.Run(() =>
    {
        using var host = session.NewHost();
        var window = host.Services.GetRequiredService<MainWindow>();
        window.ViewModel.IsSessionChecked = true;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<HyperlinkButton>().SelectMany(link => link.GetVisualDescendants().OfType<TextBlock>()).ToList();
        Assert.Contains(texts, text => text.Text == "Help");
        Assert.All(texts, text => Assert.True(text.TextDecorations is null or { Count: 0 }, $"{text.Text} is underlined"));
        window.Hide();
    });

    [Fact]
    public void The_decorations_host_renders_grayscale_text_like_the_window() => session.Run(() =>
    {
        using var host = session.NewHost();
        var window = host.Services.GetRequiredService<MainWindow>();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var decorationsHost = window.GetVisualParent();
        Assert.NotNull(decorationsHost);
        Assert.Equal(TextRenderingMode.Antialias, TextOptions.GetTextRenderingMode(decorationsHost));
        window.Hide();
    });

    [Fact]
    public void Shell_renders_a_frame_at_1280_by_820() => session.Run(() =>
    {
        using var host = session.NewHost();
        var window = host.Services.GetRequiredService<MainWindow>();
        LiveUpdatesPreview.Apply(window.ViewModel);
        window.Width = 1280;
        window.Height = 820;
        window.Show();

        var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Assert.Equal(1280, frame.PixelSize.Width);
        Assert.Equal(820, frame.PixelSize.Height);
        if (Environment.GetEnvironmentVariable("STEWARD_SHELL_PNG") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            frame.Save(path, PngBitmapEncoderOptions.Default);
        }

        TextCentring.Verify(window, "shell");
        window.Hide();
    });
}
