using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Steward.App.Services;
using Steward.App.ViewModels;
using Steward.Core;
using Steward.Desktop.Services;
using Steward.Desktop.Views;

namespace Steward.Desktop.Tests;

public sealed class DialogsATests(AvaloniaSession session)
{
    private static readonly string? RenderFolder = Environment.GetEnvironmentVariable("STEWARD_DIALOGS_A_DIR");

    private static ChangelogRun Text(string text) => new(text);

    private static ChangelogBlock Block(string kind, int level, int depth, params ChangelogRun[] runs) => new(kind, level, depth, runs);

    private static readonly IReadOnlyList<ChangelogBlock> SampleChangelog =
    [
        Block("heading", 1, 0, Text("0.24.0")),
        Block("heading", 2, 0, Text("Features")),
        Block("item", 0, 0, Text("the Addons table keeps its scroll position "), new ChangelogRun("(#412)", "https://github.com/hoobio/steward-companion/pull/412")),
        Block("item", 0, 1, Text("nested under it, with a "), new ChangelogRun("blocked link", "javascript:alert(1)"), Text(" and a plain tail that is long enough to wrap onto a second line of the dialog")),
        Block("item", 0, 2, Text("two levels deep")),
        Block("heading", 3, 0, Text("Bug fixes")),
        Block("text", 0, 0, Text("A paragraph with "), new ChangelogRun("a link", "https://example.com/changes"), Text(" inside it.")),
        new ChangelogBlock("item", 0, 0, [Text("an older entry shown dimmed")]) { Muted = true },
    ];

    private static void Settle()
    {
        for (var i = 0; i < 30; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private static void Render(FAContentDialog dialog, string name, Action<Window>? beforeCapture = null)
    {
        var window = new Window { Width = 720, Height = 640 };
        window.Show();
        var shown = AppDialogs.ShowAsync(dialog, window);
        AvaloniaSession.WaitFor(() => dialog.IsLoaded);
        Assert.False(shown.IsFaulted, shown.Exception?.ToString());
        try
        {
            Settle();
            beforeCapture?.Invoke(window);
            Dispatcher.UIThread.RunJobs();
            Settle();
            var frame = window.CaptureRenderedFrame();
            if (RenderFolder is { Length: > 0 })
            {
                Directory.CreateDirectory(RenderFolder);
                frame!.Save(Path.Combine(RenderFolder, $"{name}.png"), PngBitmapEncoderOptions.Default);
            }

            TextCentring.Verify(window, name);
        }
        finally
        {
            AvaloniaSession.CloseDialog(dialog, shown);
            window.Close();
        }
    }

    [Fact]
    public void Changelog_dialog_renders_headings_nested_items_and_http_links() => session.Run(() =>
    {
        var dialog = new ChangelogDialog("Changelog for Steward 0.24.0", SampleChangelog);

        Render(dialog, "changelog", window =>
        {
            var view = dialog.GetVisualDescendants().OfType<ChangelogView>().Single();
            Assert.Equal(SampleChangelog.Count, view.Children.Count);
            Assert.Equal(0.5, view.Children[^1].Opacity);
        });
    });

    [Fact]
    public void Changelog_dialog_with_no_content_renders() => session.Run(() =>
    {
        Render(new ChangelogDialog("Changelog", []), "changelog-empty");
    });

    private AddonChannelViewModel Channel(string? current, bool withPreRelease, bool withChangelog)
    {
        var platform = new AvaloniaPlatform();
        var store = new AppStateStore(["steward"], Path.Combine(session.NewFolder(), "state.json"));
        var addon = new ManagedAddon("steward", "Steward", "https://addon.hoobi.io/steward/", Name: "Steward");
        var channel = new AddonChannelViewModel(addon, store, (_, _) => { }, platform)
        {
            Hint = "Pre-release builds are published on every push and can be unstable.",
        };
        var release = new AddonRelease("0.12.0", null, null, 0, DateTimeOffset.Now.AddDays(-3), Changelog: withChangelog ? SampleChangelog : null);
        var pre = new AddonRelease("0.13.0-pre.44", null, null, 0, DateTimeOffset.Now.AddHours(-5), Changelog: withChangelog ? SampleChangelog : null);
        channel.Apply(new AddonChannelStatus(
            new Dictionary<string, AddonRelease?> { ["release"] = release, ["pre-release"] = withPreRelease ? pre : null },
            current,
            null));
        return channel;
    }

    [Fact]
    public void Channel_dialog_marks_the_current_channel_and_changes_the_primary_text_on_selection() => session.Run(() =>
    {
        var dialog = new ReleaseChannelDialog(Channel("release", withPreRelease: true, withChangelog: true));

        Render(dialog, "channel", window =>
        {
            var radios = dialog.GetVisualDescendants().OfType<RadioButton>().ToList();
            Assert.Equal(2, radios.Count);
            Assert.True(radios[0].IsChecked);
            Assert.Equal("Save", dialog.PrimaryButtonText);
            radios[1].IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Use pre-release", dialog.PrimaryButtonText);
            radios[0].IsChecked = true;
            Assert.Equal("Save", dialog.PrimaryButtonText);
        });
    });

    [Fact]
    public void Channel_dialog_shows_a_changelog_panel_that_toggles_and_a_disabled_option_without_releases() => session.Run(() =>
    {
        var dialog = new ReleaseChannelDialog(Channel("release", withPreRelease: false, withChangelog: true));

        Render(dialog, "channel-changelog", window =>
        {
            var radios = dialog.GetVisualDescendants().OfType<RadioButton>().ToList();
            Assert.False(radios[1].IsEnabled);
            var buttons = dialog.GetVisualDescendants().OfType<Button>().Where(button => button.IsVisible && (Avalonia.Automation.AutomationProperties.GetName(button) ?? "").StartsWith("Changelog for", StringComparison.Ordinal)).ToList();
            Assert.Single(buttons);
            var panel = dialog.FindControl<StackPanel>("ChangelogPanel")!;
            Assert.False(panel.IsVisible);
            buttons[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(panel.IsVisible);
            Assert.NotEmpty(dialog.FindControl<ChangelogView>("ChangelogHost")!.Children);
        });
    });

    [Fact]
    public void Channel_dialog_without_changelog_has_no_changelog_button() => session.Run(() =>
    {
        var dialog = new ReleaseChannelDialog(Channel("pre-release", withPreRelease: true, withChangelog: false));

        Render(dialog, "channel-plain", window =>
        {
            Assert.DoesNotContain(dialog.GetVisualDescendants().OfType<Button>(), button => button.IsVisible && (Avalonia.Automation.AutomationProperties.GetName(button) ?? "").StartsWith("Changelog for", StringComparison.Ordinal));
        });
    });

    private static GuildOptionViewModel Guild(AvaloniaPlatform platform, string id, string name, int members, string role) =>
        new(new AdminGuild(id, name, null, members, null), role, platform);

    [Fact]
    public void Guild_dialog_with_several_guilds_preselects_and_returns_the_choice() => session.Run(() =>
    {
        var platform = new AvaloniaPlatform();
        var guilds = new[] { Guild(platform, "1", "Forever Guild", 12, "Officer"), Guild(platform, "2", "Second Guild With A Quite Long Name That Should Be Trimmed At The End", 1, "Member"), Guild(platform, "3", "Third", 340, "Member") };
        var dialog = new ChooseGuildDialog(guilds, guilds[1]);

        Render(dialog, "guilds", window =>
        {
            var radios = dialog.GetVisualDescendants().OfType<RadioButton>().ToList();
            Assert.Equal(3, radios.Count);
            Assert.True(radios[1].IsChecked);
            Assert.Same(guilds[1], dialog.Selected);
            radios[2].IsChecked = true;
            Assert.Same(guilds[2], dialog.Selected);
            Assert.False(radios[1].IsChecked);
        });
    });

    [Fact]
    public void Guild_dialog_with_one_guild_renders() => session.Run(() =>
    {
        var platform = new AvaloniaPlatform();
        var guild = Guild(platform, "1", "Forever Guild", 1, "Officer");
        var dialog = new ChooseGuildDialog([guild], guild);

        Render(dialog, "guild-single");

        Assert.Same(guild, dialog.Selected);
    });

    private static WowInstallViewModel Install(IServiceProvider services, string path, string? product, string? label)
    {
        var install = new WowInstall(Path.GetDirectoryName(path)!, "_forever_", path, Path.Combine(path, "Interface", "AddOns"), product, "1.0.0.1", "World of Warcraft: Forever");
        return new WowInstallViewModel(
            install,
            "Forever",
            label,
            [],
            (_, _) => [],
            (_, _) => Task.FromResult<IReadOnlyList<ProviderAddonRecord>>([]),
            (_, _) => false,
            (_, _) => { },
            _ => Task.FromResult(true),
            services.GetRequiredService<AddonUpdater>(),
            services.GetRequiredService<AppStateStore>(),
            (_, _) => Task.FromResult(true),
            () => false,
            _ => { },
            (_, _, _) => Task.FromResult(true),
            _ => { },
            _ => { },
            (_, _) => Task.CompletedTask,
            (_, _, _, _, _, _) => Task.CompletedTask,
            () => null,
            services.GetRequiredService<AvaloniaPlatform>(),
            NullLogger.Instance);
    }

    [Fact]
    public void Edit_install_dialog_shows_the_detected_product_and_a_label_placeholder() => session.Run(() =>
    {
        using var host = session.NewHost();
        var main = host.Services.GetRequiredService<MainViewModel>();
        var path = Path.Combine(session.NewFolder(), "_forever_");
        var dialog = new EditInstallDialog(main, Install(host.Services, path, "wowf", null));

        Render(dialog, "edit-install", window =>
        {
            var box = dialog.GetVisualDescendants().OfType<TextBox>().First(text => text.Name != "PART_EditableTextBox");
            Assert.Equal("", box.Text);
            Assert.False(string.IsNullOrEmpty(box.PlaceholderText));
            var combo = dialog.GetVisualDescendants().OfType<ComboBox>().Single();
            Assert.Equal("wowf", ((GameVersionOption)combo.SelectedItem!).Code);
            Assert.True(dialog.IsPrimaryButtonEnabled);
            box.Text = "Räid";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Rid", box.Text);
        });
    });

    [Fact]
    public void Edit_install_dialog_without_a_game_version_refuses_to_save_and_shows_the_error() => session.Run(() =>
    {
        using var host = session.NewHost();
        var main = host.Services.GetRequiredService<MainViewModel>();
        var path = Path.Combine(session.NewFolder(), "_unknown_");
        var dialog = new EditInstallDialog(main, Install(host.Services, path, null, "My install"));

        Render(dialog, "edit-install-no-version", window =>
        {
            var combo = dialog.GetVisualDescendants().OfType<ComboBox>().Single();
            Assert.Null(combo.SelectedItem);
            Assert.Equal("My install", dialog.GetVisualDescendants().OfType<TextBox>().First(text => text.Name != "PART_EditableTextBox").Text);
            var error = dialog.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "GameVersionError");
            Assert.False(error.IsVisible);
            var primary = dialog.GetVisualDescendants().OfType<Button>().First(button => button.Name == "PrimaryButton");
            primary.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(error.IsVisible);
        });
    });
}
