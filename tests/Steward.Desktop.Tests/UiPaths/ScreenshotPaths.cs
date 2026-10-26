using Steward.App;
using Steward.App.Services;
using Steward.App.ViewModels;
using Steward.Core;

using static Steward.Desktop.Tests.UiPaths.UiPathWorld;

namespace Steward.Desktop.Tests.UiPaths;

public sealed class ScreenshotPaths(AvaloniaSession session)
{
    [Theory]
    [InlineData(true, "Steward.exe", "--screenshot")]
    [InlineData(true, "--SCREENSHOT")]
    [InlineData(false, "--screenshots")]
    [InlineData(false, "--guides-preview=current")]
    [InlineData(false)]
    public void Screenshot_switch_is_read_from_the_command_line(bool expected, params string[] arguments) =>
        Assert.Equal(expected, ScreenshotPreview.IsRequested(arguments));

    [Fact]
    public void Screenshot_title_has_no_build_suffix()
    {
        Assert.Equal("Steward", AppInfo.TitleFor(screenshot: true));
        Assert.Equal(AppInfo.WindowTitle, AppInfo.TitleFor(screenshot: false));
    }

    [Fact]
    public void Screenshot_start_shows_a_made_up_identity_and_guild() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "screenshot", screenshot: true);

        Assert.Equal("Adventurer", world.Main.UserName);
        Assert.Null(world.Main.UserHandle);
        Assert.Null(world.Main.AvatarImage);
        Assert.Equal("Steward Guild", world.Main.SelectedGuild?.Label);
        Assert.Null(world.Main.SelectedGuild?.Icon);
        Assert.Equal("Steward Guild", world.Main.Sync.SubtitleGuild);
        Assert.DoesNotContain("Hoobi", Text(world.Window), StringComparison.Ordinal);
        Assert.DoesNotContain("Forever Guild", Text(world.Window), StringComparison.Ordinal);
    });

    [Fact]
    public void Screenshot_start_shows_the_signed_in_guides_preview() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "screenshot-guides", screenshot: true);

        Assert.True(world.Main.RestedXp.IsPreview);
        Assert.True(world.Main.RestedXp.IsSignedIn);
        Assert.Equal("adventurer@example.com", world.Main.RestedXp.SignedInAs);
        Assert.Equal("Adventurer#1234", world.Main.RestedXp.BattleTag);
        Assert.True(world.Main.GuidesVisibility);
        var guide = Assert.IsType<RestedXpInstallViewModel>(world.Main.SelectedGuide);
        Assert.False(guide.LoadingVisibility);
        Assert.NotEmpty(guide.SelectedRows);
        Assert.All(guide.SelectedRows, row => Assert.Equal(GuideRowState.InGame, row.State));
    });

    [Fact]
    public void Screenshot_start_leaves_hoobi_scripts_out_of_the_addons() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "screenshot-addons", screenshot: true);

        var rows = world.Main.Installs.SelectMany(install => install.TableRows).ToList();
        Assert.Contains(rows, row => row.DisplayName == "BugSack");
        Assert.DoesNotContain(rows, row => row.DisplayName.Contains("Hoobi", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(world.Main.AddonChannels, channel => channel.AddonId == ScreenshotPreview.HiddenAddonId);
        Assert.Equal(0, world.Main.Installs.Sum(install => install.HiddenCount));
    });

    [Fact]
    public void Screenshot_start_writes_nothing_to_the_stored_state() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "screenshot-state", screenshot: true);

        Assert.NotNull(world.Store.Load().AddonCatalogue);
        Assert.Null(new AppStateStore([], Path.Combine(world.DataFolder, "state.json")).Load().AddonCatalogue);
    });
}
