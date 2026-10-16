using System.ComponentModel;

using Avalonia.Headless;
using Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;

using Steward.App.ViewModels;
using Steward.Core;
using Steward.Desktop.Services;
using Steward.Desktop.Views;

namespace Steward.Desktop.Tests;

public sealed class ColumnFitTests(AvaloniaSession session)
{
    private const int PassesPerWidth = 2;
    private const int MaxModeChangesPerSettle = 64;

    private static int s_modeChanges;

    [Fact]
    public void Addons_table_layout_is_stable_at_each_width_and_changes_once_each_way() => session.Run(() =>
    {
        var temp = session.NewFolder();
        var platform = new AvaloniaPlatform();
        using var host = session.NewHost(platform);
        platform.Http = null;
        var main = host.Services.GetRequiredService<MainViewModel>();
        var window = host.Services.GetRequiredService<MainWindow>();
        window.MinWidth = 0;
        window.Width = 1000;
        window.Height = 820;
        window.Show();
        main.UserName = "Hoobi";
        main.IsSignedIn = true;
        main.IsAuthorized = true;
        var install = HomePageRenderTests.Seed(main, host.Services.GetRequiredService<AppStateStore>(), temp);
        List<AddonRowViewModel> rows = [.. main.TableRows.OfType<AddonRowViewModel>()];
        foreach (var row in rows)
        {
            row.PropertyChanged += CountModeChange;
        }

        Settle();
        Assert.Contains(rows, row => row.VersionPairVisibility);
        Assert.Contains(AssertStable(window, rows, (530, 575), (685, 715)), mode => mode.Contains('S', StringComparison.Ordinal));

        install.ApplyStatus(
            new Dictionary<string, AddonChannelStatus>
            {
                ["steward"] = Status("pre-release", Release("release", "0.0.0-a1b2c3d", 3), Release("pre-release", "0.0.0-d3f1d44", 1)),
                ["hoobiscripts"] = Status("pre-release", null, Release("pre-release", "0.0.0-ec7e7a3", 0)),
                ["rxpauthor"] = Status("pre-release", null, Release("pre-release", "0.0.0-7800414", 1)),
                ["restedxp"] = Status("release", Release("release", "v4.11.22", 4), null),
                ["questie"] = Status("release", null, null),
            },
            background: false);
        Settle();
        Assert.Single(rows, row => row.VersionPairVisibility);
        AssertStable(window, rows, (650, 680));

        foreach (var row in rows)
        {
            row.PropertyChanged -= CountModeChange;
        }

        window.Hide();
        main.Installs.Remove(install);
    });

    private static void CountModeChange(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AddonRowViewModel.IsCompact) or nameof(AddonRowViewModel.IsVersionStacked) && ++s_modeChanges > MaxModeChangesPerSettle)
        {
            throw new InvalidOperationException($"The Addons table changed layout {s_modeChanges} times without the window changing size");
        }
    }

    private static AddonChannelStatus Status(string channel, AddonRelease? release, AddonRelease? preRelease) =>
        new(new Dictionary<string, AddonRelease?> { ["release"] = release, ["pre-release"] = preRelease }, channel, null);

    private static AddonRelease Release(string channel, string version, int daysAgo) =>
        new(version, $"{channel}.zip", null, 0, DateTimeOffset.Now.AddDays(-daysAgo).AddMinutes(-5));

    private static List<string> AssertStable(MainWindow window, List<AddonRowViewModel> rows, params (int From, int To)[] ranges)
    {
        var widths = ranges.SelectMany(range => Enumerable.Range(range.From, range.To - range.From + 1)).ToList();
        var down = Sweep(window, rows, Enumerable.Reverse(widths));
        var up = Sweep(window, rows, widths);
        Assert.Contains(down, mode => mode.Contains('C', StringComparison.Ordinal));
        Assert.Contains(down, mode => mode.Contains('c', StringComparison.Ordinal));
        foreach (var modes in new[] { down, up })
        {
            for (var flag = 0; flag < rows.Count * 2; flag++)
            {
                var flips = modes.Zip(modes.Skip(1)).Count(pair => pair.First[flag] != pair.Second[flag]);
                Assert.True(flips <= 1, $"{rows[flag / 2].DisplayName}'s {(flag % 2 == 0 ? "compact" : "stacked")} mode changed {flips} times in one sweep");
            }
        }

        return up;
    }

    private static string Mode(List<AddonRowViewModel> rows) =>
        string.Concat(rows.Select(row => $"{(row.IsCompact ? 'C' : 'c')}{(row.IsVersionStacked ? 'S' : 's')}"));

    private static List<string> Sweep(MainWindow window, List<AddonRowViewModel> rows, IEnumerable<int> widths)
    {
        var modes = new List<string>();
        foreach (var width in widths)
        {
            window.Width = width;
            Settle();
            var seen = new HashSet<string>();
            for (var pass = 0; pass < PassesPerWidth; pass++)
            {
                Settle(ticks: 1);
                seen.Add(Mode(rows));
            }

            Assert.True(seen.Count == 1, $"At {width}px the table changed between layout passes: {string.Join(" / ", seen)}");
            modes.Add(seen.Single());
        }

        return modes;
    }

    private static void Settle(int ticks = 2)
    {
        s_modeChanges = 0;
        for (var pass = 0; pass < ticks; pass++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
