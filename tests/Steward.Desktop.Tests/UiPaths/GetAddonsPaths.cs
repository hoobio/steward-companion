using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using Steward.App.ViewModels;
using Steward.Desktop.Views;

using static Steward.Desktop.Tests.UiPaths.UiPathWorld;

namespace Steward.Desktop.Tests.UiPaths;

public sealed class GetAddonsPaths(AvaloniaSession session)
{
    [Fact]
    public void P036_P041_get_addons() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "get-addons");
        GetAddonsViewModel? model = null;

        world.Step(86, "subtle button hover (Get addons)", () =>
        {
            var button = world.Named("Get addons");
            Hover(button);
            Assert.True(IsOver(button));
        });

        world.Step(36, "Get addons opens with the search box focused", () =>
        {
            Click(world.Named("Get addons"));
            var dialog = world.WaitForDialog<GetAddonsDialog>();
            model = (GetAddonsViewModel)dialog.DataContext!;
            Assert.Equal("Search addons", AutomationProperties.GetName((Control)world.Focused!));
        });

        world.Step(37, "popular results load", () =>
        {
            WaitFor(() => !model!.IsLoading && model.Results.Count == 12);
            Assert.Contains("Popular addon 1", Text(world.OpenDialog()), StringComparison.Ordinal);
        });

        world.Step(38, "typing before any click goes into the search box", () =>
        {
            world.Type("weakauras");
            Assert.Equal("weakauras", model!.Query);
            WaitFor(() => !model.IsLoading && model.Results.Count == 30 && model.Results[0].Name == "weakauras addon 1");
        });

        world.Step(39, "sources ComboBox opens", () =>
        {
            var source = world.Find<ComboBox>(combo => AutomationProperties.GetName(combo) == "Source");
            Click(source);
            Assert.True(source.IsDropDownOpen);
            Assert.NotNull(world.TryFind<ComboBoxItem>(item => item.Content as string == "CurseForge"));
        });

        world.Step(40, "source CurseForge filters and a new search resets the scroll", () =>
        {
            Click(world.Find<ComboBoxItem>(item => item.Content as string == "CurseForge"));
            var source = world.Find<ComboBox>(combo => AutomationProperties.GetName(combo) == "Source");
            Assert.False(source.IsDropDownOpen);
            Assert.Equal(1, source.SelectedIndex);
            Assert.Equal(30, model!.Results.Count);
        });

        world.Step("X3", "Get addons search scroll resets", () =>
        {
            var scroller = world.ByName<ScrollViewer>("ResultsScroller");
            WaitFor(() => scroller.Extent.Height > scroller.Viewport.Height);
            for (var notch = 0; notch < 4; notch++)
            {
                Wheel(scroller, -1);
            }

            WaitFor(() => scroller.Offset.Y > 0);
            world.Shot("X3", "scrolled");
            Click(world.Find<TextBox>(box => AutomationProperties.GetName(box) == "Search addons"));
            for (var i = 0; i < "weakauras".Length; i++)
            {
                world.Press(Key.Back);
            }

            world.Type("map");
            WaitFor(() => !model!.IsLoading && model.Results.Count == 30 && model.Results[0].Name == "map addon 1");
            WaitFor(() => scroller.Offset.Y == 0);
        });

        world.Step(41, "Escape closes Get addons", () =>
        {
            world.Press(Key.Escape);
            world.WaitForNoDialog();
        });
    });
}
