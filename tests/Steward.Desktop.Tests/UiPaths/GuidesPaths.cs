using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using Steward.App.Services;
using Steward.Desktop.Views;

using static Steward.Desktop.Tests.UiPaths.UiPathWorld;

namespace Steward.Desktop.Tests.UiPaths;

public sealed class GuidesPaths(AvaloniaSession session)
{
    private static readonly (int Id, string Scenario, string Expected)[] PageScenarios =
    [
        (114, "none", "No guides on this account"),
        (115, "expired", "Forever Leveling Guide - Both Factions"),
        (116, "current", "Mists of Pandaria Guide - Bundle"),
        (117, "downloading", "Downloading"),
        (118, "written", "Written · imports on next login or /reload"),
        (119, "failed", "Failed"),
        (120, "newer", "Needs a newer guide"),
        (121, "unfinished", "Not finished"),
        (122, "ingame", "Up to date"),
    ];

    private static readonly (int Id, string Scenario, string Expected)[] DialogScenarios =
    [
        (123, "signin", "Sign in"),
        (124, "signin-error", "Wrong username or password"),
        (125, "signin-busy", "Sign in"),
        (126, "mfa", "Verify"),
        (127, "mfa-error", "That code was not accepted"),
        (128, "mfa-busy", "Verify"),
    ];

    [Fact]
    public void P100_P102_restedxp_sign_in() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "guides-sign-in");
        Button SignIn() => world.Find<Button>(button => button.Content as string == "Sign in to RestedXP");

        world.Step(100, "RestedXP sign-in dialog", () =>
        {
            Click(SignIn());
            world.WaitForDialog<RestedXpSignInDialog>();
            Assert.IsType<TextBox>(world.Focused);
        });

        world.Step(101, "Escape closes the sign-in dialog", () =>
        {
            world.Press(Key.Escape);
            world.WaitForNoDialog();
            Assert.False(world.Main.RestedXp.IsSignedIn);
        });

        world.Step(102, "Escape after typing in the password box closes it", () =>
        {
            Click(SignIn());
            var dialog = world.WaitForDialog<RestedXpSignInDialog>();
            Click(world.Find<TextBox>(box => box.PasswordChar != default && dialog.IsVisualAncestorOf(box)));
            world.Type("hunter2");
            Assert.Equal("hunter2", world.Main.RestedXp.PasswordInput);
            world.Press(Key.Escape);
            world.WaitForNoDialog();
        });
    });

    [Fact]
    public void P114_P128_X1_guides_previews() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "guides-previews");

        world.Step("X1", "guides loading row", () =>
        {
            GuidesPreview.Apply(world.Main, "loading");
            world.Navigate("Guides");
            Assert.Contains("Loading your guides from RestedXP…", Text(world.Window.CurrentPage!), StringComparison.Ordinal);
        });

        foreach (var (id, scenario, expected) in PageScenarios)
        {
            world.Step(id, $"guides preview {scenario}", () =>
            {
                GuidesPreview.Apply(world.Main, scenario);
                Pump();
                Assert.IsType<GuidesPage>(world.Window.CurrentPage);
                Assert.Contains(expected, Text(world.Window.CurrentPage!), StringComparison.Ordinal);
                if (scenario == "expired")
                {
                    Assert.True(world.ByName<Control>("SessionExpiredBar").IsEffectivelyVisible);
                }
            });
        }

        foreach (var (id, scenario, expected) in DialogScenarios)
        {
            world.Step(id, $"guides preview {scenario}", () =>
            {
                GuidesPreview.Apply(world.Main, scenario);
                var shown = world.Window.ShowRestedXpSignInAsync();
                var dialog = world.WaitForDialog<RestedXpSignInDialog>();
                Assert.Contains(expected, Text(dialog), StringComparison.Ordinal);
                world.Shot(id.ToString(System.Globalization.CultureInfo.InvariantCulture), scenario);
                world.Main.RestedXp.IsBusy = false;
                world.Press(Key.Escape);
                WaitFor(() => shown.IsCompleted);
            });
        }
    });
}
