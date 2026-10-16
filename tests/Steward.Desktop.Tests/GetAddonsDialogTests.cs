using System.Reflection;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Steward.App.ViewModels;
using Steward.Core;
using Steward.Desktop.Services;
using Steward.Desktop.Views;

namespace Steward.Desktop.Tests;

public sealed class GetAddonsDialogTests(AvaloniaSession session)
{
    [Fact]
    public void Get_addons_dialog_renders_its_states() => session.Run(() =>
    {
        using var host = session.NewHost();
        var services = host.Services;
        var main = services.GetRequiredService<MainViewModel>();
        var platform = services.GetRequiredService<AvaloniaPlatform>();
        var temp = session.NewFolder();
        var flavour = Path.Combine(temp, "_forever_");
        Directory.CreateDirectory(Path.Combine(flavour, "Interface", "AddOns"));
        var install = new WowInstall(temp, "_forever_", flavour, Path.Combine(flavour, "Interface", "AddOns"), "wowf", "1.0.0", "World of Warcraft: Forever");
        var added = (WowInstallViewModel)typeof(MainViewModel)
            .GetMethod("AddInstall", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(main, [install, false])!;
        var viewModel = new GetAddonsViewModel(main, added, 88568, services.GetRequiredService<StewardClient>(), NullLogger.Instance);
        var dialog = new GetAddonsDialog(viewModel);

        var window = new Window { Width = 1280, Height = 820, Background = (IBrush)Application.Current!.FindResource("WindowGroundBrush")! };
        window.Show();
        var shown = AppDialogs.ShowAsync(dialog, window);
        AvaloniaSession.WaitFor(() => dialog.IsLoaded);

        Set(viewModel, "Heading", "Popular for World of Warcraft: Forever");
        Set(viewModel, "IsLoading", true);
        Save(window, "get-addons-loading");
        var ring = dialog.GetVisualDescendants().OfType<ProgressRing>().Single(found => found.Name == "LoadingRing");
        Assert.True(ring.IsEffectivelyVisible);
        Assert.True(ring.IsActive);
        Assert.Equal(16, ring.Bounds.Width);

        Set(viewModel, "IsLoading", false);
        var installing = Result(10, "Questie", "Questie Team", "An addon with a very long summary that keeps going so that it has to be trimmed with an ellipsis at the end of the row", "v10.2.1");
        var failed = Result(11, "Deadly Boss Mods", "MysticalOS", "Boss alerts and timers", "v11.0.5");
        var installed = Result(12, "Details!", "Terciob", "Damage meter", "v12.9", installedAlready: true);
        var linked = Result(13, "Atlas Loot", "Lag", "Loot browser", "v1.4", allowDistribution: false);
        foreach (var row in new[] { installing, failed, installed, linked, Result(14, "BugSack", "Funkydude", "Bug viewer", "v9.1") })
        {
            viewModel.Results.Add(row);
        }

        Set(installing, "IsInstalling", true);
        Set(failed, "Failure", "Could not install Deadly Boss Mods: the download failed.");
        Dispatcher.UIThread.RunJobs();
        Save(window, "get-addons-results");

        Assert.Equal("Install", ButtonTexts(dialog).First(text => text == "Install"));
        Assert.Contains(dialog.GetVisualDescendants().OfType<HyperlinkButton>(), link => link.IsVisible && (link.Content as TextBlock)?.Text == "Available on CurseForge");
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), text => text.IsVisible && text.Text == "Installed");
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), text => text.IsVisible && text.Text == "Installing");

        Set(viewModel, "IsSearchUnavailable", true);
        Set(viewModel, "Error", "Could not reach CurseForge: The request timed out.");
        Dispatcher.UIThread.RunJobs();
        Save(window, "get-addons-unavailable-error");

        Set(viewModel, "Error", null);
        Set(viewModel, "IsSearchUnavailable", false);
        viewModel.Results.Clear();
        Set(viewModel, "HasNoResults", true);
        Set(viewModel, "Heading", "0 results for “zzz”");
        Dispatcher.UIThread.RunJobs();
        Save(window, "get-addons-no-results");

        AvaloniaSession.CloseDialog(dialog, shown);
        window.Close();
        Assert.NotNull(platform);
    });

    [Fact]
    public void A_new_result_set_scrolls_the_list_back_to_the_top() => session.Run(() =>
    {
        using var host = session.NewHost();
        var services = host.Services;
        var main = services.GetRequiredService<MainViewModel>();
        var temp = session.NewFolder();
        var flavour = Path.Combine(temp, "_forever_");
        Directory.CreateDirectory(Path.Combine(flavour, "Interface", "AddOns"));
        var install = new WowInstall(temp, "_forever_", flavour, Path.Combine(flavour, "Interface", "AddOns"), "wowf", "1.0.0", "World of Warcraft: Forever");
        var added = (WowInstallViewModel)typeof(MainViewModel)
            .GetMethod("AddInstall", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(main, [install, false])!;
        var viewModel = new GetAddonsViewModel(main, added, 88568, services.GetRequiredService<StewardClient>(), NullLogger.Instance);
        var dialog = new GetAddonsDialog(viewModel);
        var window = new Window { Width = 1280, Height = 820 };
        window.Show();
        var shown = AppDialogs.ShowAsync(dialog, window);
        AvaloniaSession.WaitFor(() => dialog.IsLoaded);
        Set(viewModel, "IsLoading", false);
        void Fill(string prefix)
        {
            viewModel.Results.Clear();
            for (var id = 1; id <= 30; id++)
            {
                viewModel.Results.Add(Result(id, $"{prefix} {id}", "Author", "Summary", "v1"));
            }

            Dispatcher.UIThread.RunJobs();
        }

        Fill("Map");
        var scroller = dialog.GetVisualDescendants().OfType<ScrollViewer>().Single(found => found.Name == "ResultsScroller");
        scroller.Offset = new Vector(0, 400);
        Dispatcher.UIThread.RunJobs();
        var scrolled = scroller.Offset.Y;

        Fill("Boss");
        var after = scroller.Offset.Y;

        AvaloniaSession.CloseDialog(dialog, shown);
        window.Close();
        Assert.True(scrolled > 0, $"the list did not scroll ({scrolled})");
        Assert.Equal(0, after);
    });

    [Fact]
    public void Match_highlight_accents_the_matched_range_and_follows_text_changes() => session.Run(() =>
    {
        var text = new TextBlock { Text = "Deadly Boss Mods" };
        MatchHighlight.SetMatch(text, new SearchMatch(7, 4));

        Assert.Equal(["Deadly ", "Boss", " Mods"], text.Inlines!.OfType<Run>().Select(run => run.Text).ToArray());
        Assert.NotNull(text.Inlines!.OfType<Run>().ElementAt(1).Foreground);

        text.Text = "Boss Timers";
        MatchHighlight.SetMatch(text, new SearchMatch(0, 4));
        Assert.Equal("Boss Timers", string.Concat(text.Inlines!.OfType<Run>().Select(run => run.Text)));
        Assert.Equal("Boss", text.Inlines!.OfType<Run>().Skip(1).First().Text);

        MatchHighlight.SetMatch(text, null);
        Assert.Equal("Boss Timers", text.Text);
        Assert.True(text.Inlines is not { Count: > 0 });
    });

    [Fact]
    public void Match_highlight_renders_on_a_bound_text_block() => session.Run(() =>
    {
        var text = new TextBlock { FontSize = 20, Foreground = Brushes.White };
        text.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(Holder.Name)));
        var holder = new Holder("Deadly Boss Mods");
        text.DataContext = holder;
        MatchHighlight.SetMatch(text, new SearchMatch(7, 4));
        var window = new Window { Width = 300, Height = 60, Background = Brushes.Black, Content = text };
        window.Show();
        Save(window, "match-highlight");
        Assert.Equal(3, text.Inlines!.Count);
        window.Close();
    });

    private sealed record Holder(string Name);

    private static CurseForgeResultViewModel Result(int id, string name, string author, string summary, string version, bool installedAlready = false, bool allowDistribution = true) =>
        new(new CurseForgeResult(id, name, summary, author, null, "https://www.curseforge.com/wow/addons/x", version, allowDistribution), installedAlready, _ => Task.FromResult<string?>(null), new AvaloniaPlatform());

    private static IEnumerable<string> ButtonTexts(Control root) =>
        root.GetVisualDescendants().OfType<Button>().Where(button => button.IsVisible).Select(button => button.Content as string ?? "").Where(text => text.Length > 0);

    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property)!.GetSetMethod(true)!.Invoke(target, [value]);

    private static void Save(TopLevel window, string name)
    {
        var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("STEWARD_DIALOGS_B_PNG_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            frame.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
        }

        TextCentring.Verify(window, name);
    }
}
