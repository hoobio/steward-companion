using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

using Steward.Desktop.Views;

using static Steward.Desktop.Tests.UiPaths.UiPathWorld;

namespace Steward.Desktop.Tests.UiPaths;

public sealed class SyncPaths(AvaloniaSession session)
{
    [Fact]
    public void P051_P055_sync_page() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "sync");
        ToggleButton Characters() => world.Find<ToggleButton>(toggle => toggle.Theme is { } && AutomationProperties.GetName(toggle) is { Length: > 0 } && toggle.FindAncestorOfType<SyncPage>() is not null);

        world.Step(51, "Sync page collapsed", () =>
        {
            world.Navigate("Sync");
            Assert.IsType<SyncPage>(world.Window.CurrentPage);
            Assert.False(Characters().IsChecked);
            Assert.Contains("Guild data in game", Text(world.Window.CurrentPage!), StringComparison.Ordinal);
        });

        world.Step(52, "Your characters expanded", () =>
        {
            Click(Characters());
            Assert.True(Characters().IsChecked);
            WaitFor(() => Text(world.Window.CurrentPage!).Contains("Hoobi", StringComparison.Ordinal));
        });

        world.Step(53, "Your characters collapsed again", () =>
        {
            Click(Characters());
            Assert.False(Characters().IsChecked);
        });

        world.Step(54, "Write again writes StewardSync.lua", () =>
        {
            var file = Path.Combine(world.BetaAddOns, "Steward", "StewardSync.lua");
            var before = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
            Click(world.Find<Button>(button => button.Content as string == "Write again"));
            WaitFor(() => File.Exists(file) && File.GetLastWriteTimeUtc(file) >= before);
            Assert.Contains("Steward.LoadSync", File.ReadAllText(file), StringComparison.Ordinal);
        });

        world.Step(55, "Sync page Tab order", () =>
        {
            Click(world.Find<TextBlock>(text => text.Text == "Sync" && text.FontSize > 20));
            var visited = new List<string>();
            for (var i = 0; i < 12; i++)
            {
                world.Press(Key.Tab);
                if (world.Focused is Control focused)
                {
                    Assert.Same(world.Window, TopLevel.GetTopLevel(focused));
                    visited.Add(AutomationProperties.GetName(focused) ?? focused.Name ?? (focused as ContentControl)?.Content as string ?? focused.GetType().Name);
                }
            }

            Assert.Contains("Switch install", visited);
            Assert.Contains("Sync", visited);
            Assert.Contains("Write again", visited);
        });
    });
}
