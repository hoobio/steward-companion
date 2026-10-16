using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Steward.App.ViewModels;
using Steward.Core;
using Steward.Desktop.Services;
using Steward.Desktop.Views;

namespace Steward.Desktop.Tests;

public sealed class AddonFoldersDialogTests(AvaloniaSession session)
{
    [Fact]
    public void Addon_folders_dialog_lists_every_folder_and_reports_a_changelog_request() => session.Run(() =>
    {
        using var host = session.NewHost();
        var platform = host.Services.GetRequiredService<AvaloniaPlatform>();
        var addOns = session.NewFolder();
        Toc(addOns, "Questie", "Questie", "v10.2.1-a1b2c3d4e5f6");
        Toc(addOns, "QuestieDB", "Questie Database", "1.0.0");
        Toc(addOns, "QuestieLocale", "Questie Locale", "1.0.0");
        Directory.CreateDirectory(Path.Combine(addOns, "Ghost"));
        var changelog = new[] { new ChangelogBlock("item", 0, 0, [new ChangelogRun("Fixed a thing")]) };
        var folders = new[]
        {
            new FolderEntry(addOns, "Questie", null, "Questie", changelog, null, "release"),
            new FolderEntry(addOns, "QuestieDB", null, null, null, new AddonNotice("lock", "Protected addon, fetched with your account."), "pre-release", "Released 3 days ago", "Monday, 12 October 2026 9:00 AM"),
            new FolderEntry(addOns, "QuestieLocale", null, null, null, new AddonNotice("info", "Locale data."), null, "Released 1 day ago", "Wednesday, 14 October 2026 9:00 AM"),
            new FolderEntry(addOns, "Ghost", null),
        };
        var dialog = new AddonFoldersDialog("Questie", folders, platform, NullLogger.Instance);

        var window = new Window { Width = 1280, Height = 820, Background = (IBrush)Application.Current!.FindResource("WindowGroundBrush")! };
        window.Show();
        var shown = AppDialogs.ShowAsync(dialog, window);
        AvaloniaSession.WaitFor(() => dialog.RowsLoaded.IsCompleted);
        AvaloniaSession.WaitFor(() => dialog.IsLoaded);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        if (Environment.GetEnvironmentVariable("STEWARD_DIALOGS_B_PNG_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            window.CaptureRenderedFrame()!.Save(Path.Combine(directory, "addon-folders.png"), PngBitmapEncoderOptions.Default);
        }

        TextCentring.Verify(window, "addon-folders");

        var texts = dialog.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
        Assert.Contains("Also installed with Questie:", texts);
        Assert.Contains("Ghost", texts);
        Assert.Contains("QuestieDB", texts);
        var changelogButton = dialog.GetVisualDescendants().OfType<Button>().Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Changelog for Questie");
        Assert.Equal(2, dialog.GetVisualDescendants().OfType<Button>().Count(button => Avalonia.Automation.AutomationProperties.GetName(button) is "Protected addon, fetched with your account." or "Locale data."));
        var released = dialog.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Text?.StartsWith("Released", StringComparison.Ordinal) == true).ToList();
        var dbVersion = dialog.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == "1.0.0");
        var channel = dialog.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == "pre-release");
        var rights = (Right(dbVersion, window), Right(released[0], window));
        var stackedGap = released[0].Bounds.Top - dbVersion.Bounds.Bottom;
        var block = (Control)released[0].Parent!;
        var blockMiddle = block.Bounds.Top + block.Bounds.Height / 2;
        var rowMiddle = ((Control)block.Parent!).Bounds.Height / 2;
        var channelGap = Left(channel, window) - rights.Item2;

        changelogButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        AvaloniaSession.WaitFor(() => shown.IsCompleted);
        window.Close();

        Assert.Equal(["Released 3 days ago", "Released 1 day ago"], released.Select(text => text.Text));
        Assert.Equal("Monday, 12 October 2026 9:00 AM", ToolTip.GetTip(released[0]));
        Assert.Equal(rights.Item1, rights.Item2, tolerance: 1);
        Assert.Equal(rowMiddle, blockMiddle, tolerance: 1);
        Assert.Equal(4, stackedGap, tolerance: 0.5);
        Assert.True(channelGap >= 24, $"channel gap {channelGap}");
        Assert.Equal("Questie", dialog.ChangelogRequest!.Folder);
        dialog.ClearChangelogRequest();
        Assert.Null(dialog.ChangelogRequest);
    });

    private static double Right(Visual visual, Visual root) => visual.TranslatePoint(new Point(visual.Bounds.Width, 0), root)!.Value.X;

    private static double Left(Visual visual, Visual root) => visual.TranslatePoint(new Point(0, 0), root)!.Value.X;

    private static void Toc(string addOns, string folder, string title, string version)
    {
        Directory.CreateDirectory(Path.Combine(addOns, folder));
        File.WriteAllText(Path.Combine(addOns, folder, $"{folder}.toc"), $"## Interface: 16001\n## Title: {title}\n## Version: {version}\n");
    }
}
