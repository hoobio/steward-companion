using System.Reflection;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using Microsoft.Extensions.DependencyInjection;

using Steward.App.ViewModels;
using Steward.Core;
using Steward.Desktop.Services;
using Steward.Desktop.Views;

namespace Steward.Desktop.Tests;

public sealed class SyncPageTests(AvaloniaSession session)
{
    [Fact]
    public void Sync_page_renders_its_states() => session.Run(() =>
    {
        using var host = session.NewHost();
        var services = host.Services;
        var main = services.GetRequiredService<MainViewModel>();
        var platform = services.GetRequiredService<AvaloniaPlatform>();
        var page = services.GetRequiredService<SyncPage>();
        var sync = page.ViewModel;
        var temp = session.NewFolder();

        var window = new Window
        {
            Width = 1216,
            Height = 772,
            Background = (IBrush)Application.Current!.FindResource("WindowGroundBrush")!,
            Content = page,
        };
        window.Show();
        Save(window, "sync-empty");

        var flavour = Path.Combine(temp, "_forever_");
        Directory.CreateDirectory(Path.Combine(flavour, "Interface", "AddOns"));
        var install = new WowInstall(temp, "_forever_", flavour, Path.Combine(flavour, "Interface", "AddOns"), "wowf", "1.0.0", "World of Warcraft: Forever");
        var added = (WowInstallViewModel)typeof(MainViewModel)
            .GetMethod("AddInstall", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(main, [install, false])!;

        window.Content = null;
        main.SelectedGuild = new GuildOptionViewModel(new AdminGuild("1", "RaT WiTH FLaMeS oN THe SiDe", null, 163, null), "Officer", platform);
        main.CharacterSyncRows.Add(new CharacterSyncRowViewModel("Forever", flavour, DateTimeOffset.Now.AddDays(-1), null));
        main.JourneyCount = "12 characters uploaded, last 29 minutes ago";
        var view = Seed(flavour, platform);
        sync.Installs.Clear();
        sync.Installs.Add(view);
        window.Content = page;
        Save(window, "sync-collapsed");

        view.IsProfessionsExpanded = true;
        Save(window, "sync-expanded");

        sync.IsUnreachable = true;
        main.JourneyError = "Journey upload failed: the server answered 503.";
        Save(window, "sync-errors");

        sync.IsUnreachable = false;
        main.JourneyError = null;
        main.SelectedInstall = added;
        UiPaths.UiPathWorld.WaitFor(() => sync.NotReleasedVisibility == (DateTimeOffset.Now < ForeverSchedule.Launch.Start) && !sync.CardsVisibility == sync.NotReleasedVisibility);
        Save(window, "sync-not-released");

        window.Content = null;
        window.Close();
        sync.IsUnreachable = false;
        main.JourneyError = null;
        main.JourneyCount = null;
        main.CharacterSyncRows.Clear();
        main.Installs.Remove(added);
        sync.Installs.Clear();
    });

    private static SyncInstallViewModel Seed(string flavour, AvaloniaPlatform platform)
    {
        var view = new SyncInstallViewModel { DisplayName = "Forever", FlavourPath = flavour, ExportState = SyncExportState.Ready };
        var roster = Dataset("guild-roster", "Guild roster", "people, 0 characters", 163, "GuildRoster.png", "In game after /reload", true, true, platform);
        var guildProfessions = Dataset("guild-professions", "Guild professions", "characters, 12 professions", 0, "GuildProfessions.png", "In game after /reload", true, false, platform);
        var mine = Dataset("professions", "Your characters", "characters synced to 1 server", 132, "Professions.png", "In sync with the guild", true, true, platform);
        view.PullDatasets.Add(roster);
        view.PullDatasets.Add(guildProfessions);
        view.Datasets.Add(roster);
        view.Datasets.Add(guildProfessions);
        view.Datasets.Add(mine);
        view.ProfessionsDataset = mine;
        view.ProfessionsCharacters.Add(new ProfessionsCharacterViewModel("Hoobi", 60, "Warrior", "Blacksmithing 300, Mining 300", ProfessionsCharacterState.Synced, null));
        view.ProfessionsCharacters.Add(new ProfessionsCharacterViewModel("Grug", 42, "Hunter", "", ProfessionsCharacterState.Pending, null));
        view.ProfessionsCharacters.Add(new ProfessionsCharacterViewModel("Thok", 30, "Shaman", "Herbalism 150", ProfessionsCharacterState.Rejected, "This character is linked to another account."));
        return view;
    }

    private static SyncDatasetViewModel Dataset(string key, string name, string unit, int count, string icon, string status, bool synced, bool first, AvaloniaPlatform platform) => new()
    {
        Key = key,
        Name = name,
        Unit = unit,
        SourceFile = "StewardSync.lua",
        LocalCount = count,
        ExportedAtText = "",
        IsStale = false,
        IsFirst = first,
        IsSynced = synced,
        IconSource = platform.ImageFromAsset(icon, null),
        StatusText = status,
    };

    private static void Save(Window window, string name)
    {
        var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("STEWARD_SYNC_PNG_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            frame.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
        }

        TextCentring.Verify(window, name);
    }
}
