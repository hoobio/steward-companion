using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

using Steward.App.ViewModels;
using Steward.Core;
using Steward.Desktop.Views;

using static Steward.Desktop.Tests.UiPaths.UiPathWorld;

namespace Steward.Desktop.Tests.UiPaths;

public sealed class AddonSettingsPaths(AvaloniaSession session)
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void X7_X8_addon_settings_page() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "addon-settings");
        var account = Path.Combine(world.Beta, "WTF", "Account", "1#1");
        var file = AddonSettings.FilePath(world.Beta, "1#1", "HoobiScripts");
        File.Copy(Fixture("HoobiScripts.lua"), file);
        Directory.CreateDirectory(Path.Combine(account, "91", "Hoobi-Spellbook"));
        var schema = SettingsSchema.Parse(File.ReadAllText(Fixture("hoobiscripts-settings-schema.json")));
        SettingsSchema? journey = SettingsSchema.Parse(File.ReadAllText(Fixture(Path.Combine("settings-schemas", "StewardJourney.json"))));
        var requested = new List<string>();
        world.Main.AddonSettings.FetchSchema = (addon, version, _) =>
        {
            requested.Add($"{addon.Id} {version}");
            return Task.FromResult(addon.Id switch
            {
                "hoobiscripts" => schema,
                "steward-journey" => journey,
                _ => null,
            });
        };
        world.Main.AddonSettings.IsRunning = _ => false;
        ToggleSwitch Toggle(string label) => world.Find<ToggleSwitch>(toggle => AutomationProperties.GetName(toggle) == label);
        void Flip(string label)
        {
            Toggle(label).BringIntoView();
            Pump();
            Click(Toggle(label));
        }

        world.Step("X7", "Addon settings page edits a setting and saves it into the account file", () =>
        {
            Assert.True(world.Main.AddonSettingsVisibility);
            world.Navigate("AddonSettings");
            Assert.IsType<AddonSettingsPage>(world.Window.CurrentPage);
            WaitFor(() => Text(world.Window.CurrentPage!).Contains("Merchant", StringComparison.Ordinal));
            Assert.Contains("hoobiscripts 0.0.0-ec7e7a3", requested);
            Assert.Contains("steward-journey 0.0.0-b2d4f68", requested);
            Assert.DoesNotContain(requested, request => request.StartsWith("bugsack", StringComparison.Ordinal));
            Assert.Contains("Journey", Text(world.Window.CurrentPage!), StringComparison.Ordinal);
            Save(world, "addon-settings");

            Assert.False(Toggle("Threat meter").IsEffectivelyEnabled);
            Assert.False(Toggle("Sell old water too").IsEffectivelyEnabled);
            Assert.True(Toggle("Hide error messages").IsChecked);
            Flip("Hide error messages");
            Assert.False(Toggle("Hide error messages").IsChecked);
            Flip("Sell old food");
            Assert.True(Toggle("Sell old water too").IsEffectivelyEnabled);
            Click(world.Find<Button>(button => button.Content as string == "Save"));
            WaitFor(() => world.Main.AddonSettings.Status is not null);

            var settings = AddonSettings.Load(file)["HoobiScriptsDB"].GetTable("settings")!;
            Assert.True(settings.Get("hideErrors") is null, world.Main.AddonSettings.Status);
            Assert.True(settings.Get("vendorOldFood")!.Boolean);
            Assert.Equal("all", settings.GetString("autoRepair"));
            Assert.Equal(160.3966953455148, settings.GetNumber("minimapAngle"));
            Assert.Contains("Grüg Aa", File.ReadAllText(file), StringComparison.Ordinal);
        });

        world.Step("X8", "New character creates the character folder and its chars entry", () =>
        {
            Assert.Equal("91", world.Main.AddonSettings.KnownRealms["Classic Beta PvP"]);
            Click(world.Find<Button>(button => button.Name == "NewCharacterButton"));
            var dialog = world.WaitForDialog();
            var boxes = dialog.GetVisualDescendants().OfType<TextBox>().ToList();
            boxes[0].Text = "grug";
            boxes[1].Text = "aa";
            dialog.GetVisualDescendants().OfType<ComboBox>().First().SelectedIndex = 0;
            Save(world, "addon-settings-new-character");
            Click(DialogButton(dialog, "PrimaryButton"));
            world.WaitForNoDialog();

            var folder = Path.Combine(account, "91", "Grug-Aa");
            Assert.True(Directory.Exists(folder));
            var entry = AddonSettings.Load(file)["HoobiScriptsDB"].GetTable("chars")!.GetTable("Grug Aa-Classic Beta PvP")!;
            Assert.Equal("WARRIOR", entry.GetString("class"));
            Assert.Equal("Grug Aa-Classic Beta PvP", world.Main.AddonSettings.SelectedCharacter?.Key);

            Assert.False(Toggle("Log your leveling journey").IsEffectivelyEnabled);
            Assert.Equal(AddonSettings.NotLoggedInNotice("Steward: Journey"), Rows(world).Single(row => row.Label == "Log your leveling journey").Description);
            Assert.False(File.Exists(AddonSettings.CharacterFilePath(folder, "StewardJourney")));
        });
    });

    private static SettingsSchema Real(string folder) => SettingsSchema.Parse(File.ReadAllText(Fixture(Path.Combine("settings-schemas", folder + ".json"))));

    private static List<SettingRowViewModel> Rows(UiPathWorld world) => [.. world.Main.AddonSettings.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Rows)];

    private static T Row<T>(UiPathWorld world, string key)
        where T : SettingRowViewModel => Rows(world).OfType<T>().First(row => row.Row.Key == key);

    private static UiPathWorld World(AvaloniaSession session, string area, Func<ManagedAddon, SettingsSchema?> schemas, bool withFile = true, string product = ForeverLaunch.BetaProduct)
    {
        var world = new UiPathWorld(session, area, withLive: product == ForeverLaunch.BetaProduct, betaProduct: product);
        if (withFile)
        {
            File.Copy(Fixture("HoobiScripts.lua"), AddonSettings.FilePath(world.Beta, "1#1", "HoobiScripts"));
        }

        Directory.CreateDirectory(Path.Combine(world.Beta, "WTF", "Account", "1#1", "91", "Hoobi-Spellbook"));
        world.Main.AddonSettings.FetchSchema = (addon, _, _) => Task.FromResult(schemas(addon));
        world.Main.AddonSettings.IsRunning = _ => false;
        return world;
    }

    private static void Open(UiPathWorld world)
    {
        world.Navigate("AddonSettings");
        WaitFor(() => world.Main.AddonSettings.Pages.Count > 0 && !world.Main.AddonSettings.IsLoading);
        Pump();
    }

    private static void Answer(UiPathWorld world, string title, string button)
    {
        var dialog = world.WaitForDialog();
        Assert.Contains(title, Text(dialog), StringComparison.Ordinal);
        Click(DialogButton(dialog, button));
        world.WaitForNoDialog();
        Pump();
    }

    [Fact]
    public void A_row_reads_and_writes_the_file_whose_toc_declares_its_global() => session.Run(() =>
    {
        using var world = World(session, "addon-settings-owner", addon => addon.Id switch
        {
            "hoobiscripts" => Real("HoobiScripts"),
            "hoobiscripts-actionbars" => Real("HoobiScripts_ActionBars"),
            _ => null,
        });
        var current = TocFile.InterfaceNumber(ClientVersion)!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        world.Toc("HoobiScripts_ActionBars", "Hoobi Scripts: Action Bars", "0.0.0-1a2b3c4", current, "## SavedVariables: HoobiScripts_ActionBarsDB\n");
        var coreFile = AddonSettings.FilePath(world.Beta, "1#1", "HoobiScripts");
        var actionBarsFile = AddonSettings.FilePath(world.Beta, "1#1", "HoobiScripts_ActionBars");
        File.WriteAllText(actionBarsFile, "HoobiScripts_ActionBarsDB = {\n[\"applyAccount\"] = false,\n}\n");
        world.Api.MeBody = FakeApi.Me("steward", "sync", "roster", "addons", "guides").Replace(
            """{"id":"restedxp",""",
            $$"""{"id":"hoobiscripts-actionbars","name":"Hoobi Scripts: Action Bars","folder_name":"HoobiScripts_ActionBars","manifest_base_url":"{{FakeApi.AddonHost}}hoobiscripts-actionbars/","source":"Steward","parent":"hoobiscripts"},{"id":"restedxp",""",
            StringComparison.Ordinal);
        world.SignIn();
        Open(world);

        Assert.False(Rows(world).Single(row => row.Row.Key == "actionBars" && row.Addon == "HoobiScripts").IsShown);
        var page = world.Main.AddonSettings.Pages.Single(page => page.Title == "Action Bars");
        var rows = page.Groups.SelectMany(group => group.Rows).ToList();
        var bars = rows.OfType<BitmaskSettingViewModel>().Single();
        Assert.Equal(63, bars.Original.Number);
        Assert.True(bars.IsEnabled, bars.Description);
        bars.Bits.Single(bit => bit.Bit == 6).IsChecked = true;
        rows.OfType<ToggleSettingViewModel>().Single(row => row.Row.Key == "applyAccount").IsOn = true;
        world.Main.AddonSettings.SaveCommand.Execute(null);
        WaitFor(() => world.Main.AddonSettings.Status is not null);

        Assert.Equal(AddonSettingsViewModel.SavedMessage, world.Main.AddonSettings.Status);
        var core = AddonSettings.Load(coreFile)["HoobiScriptsDB"].GetTable("settings")!;
        Assert.Equal(127, core.GetNumber("actionBars"));
        Assert.True(core.Get("questMobSparkles")!.Boolean);
        var actionBars = AddonSettings.Load(actionBarsFile);
        Assert.Equal(["HoobiScripts_ActionBarsDB"], actionBars.Keys);
        Assert.Null(actionBars["HoobiScripts_ActionBarsDB"].Get("applyAccount"));
    });

    [Fact]
    public void A_schema_for_another_folder_or_with_a_bad_global_is_refused() => session.Run(() =>
    {
        var journey = SettingsSchema.Parse(File.ReadAllText(Fixture(Path.Combine("settings-schemas", "StewardJourney.json")))
            .Replace("\"StewardJourneyDB\"", "\"..\\\\..\\\\StewardJourneyDB\"", StringComparison.Ordinal));
        using var world = World(session, "addon-settings-refused", addon => addon.Id switch
        {
            "hoobiscripts" => Real("HoobiScripts") with { Folder = "HoobiScripts_Sniper" },
            "steward-journey" => journey,
            _ => null,
        });
        Open(world);

        var page = Assert.Single(world.Main.AddonSettings.Pages);
        Assert.Equal(AddonSettingsViewModel.NotAvailableMessage, page.Message);
        Assert.Empty(page.Groups);
        var log = string.Concat(Directory.GetFiles(Path.Combine(world.DataFolder, "logs")).Select(file =>
        {
            using var reader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            return reader.ReadToEnd();
        }));
        Assert.Contains("WRN", log, StringComparison.Ordinal);
        Assert.Contains("refused: its folder HoobiScripts_Sniper is not HoobiScripts", log, StringComparison.Ordinal);
        Assert.Contains(@"refused: row enabled names the global ..\..\StewardJourneyDB", log, StringComparison.Ordinal);
    });

    [Fact]
    public void Unsaved_edits_ask_before_they_are_dropped() => session.Run(() =>
    {
        var schema = SettingsSchema.Parse(File.ReadAllText(Fixture("hoobiscripts-settings-schema.json")));
        using var world = World(session, "addon-settings-unsaved", addon => addon.Id == "hoobiscripts" ? schema : null);
        var file = AddonSettings.FilePath(world.Beta, "1#1", "HoobiScripts");
        var settings = world.Main.AddonSettings;
        Open(world);
        var first = settings.SelectedCharacter!;
        var other = settings.Characters.First(option => option != first);

        Row<ToggleSettingViewModel>(world, "hideErrors").IsOn = false;
        settings.SelectedCharacter = other;
        Answer(world, "Save your changes?", "CloseButton");
        Assert.Equal(first, settings.SelectedCharacter);
        Assert.True(settings.HasChanges);
        Assert.False(Row<ToggleSettingViewModel>(world, "hideErrors").IsOn);

        settings.SelectedCharacter = other;
        Answer(world, "Save your changes?", "SecondaryButton");
        Assert.Equal(other, settings.SelectedCharacter);
        Assert.False(settings.HasChanges);
        Assert.True(Row<ToggleSettingViewModel>(world, "hideErrors").IsOn);

        Row<ToggleSettingViewModel>(world, "hideErrors").IsOn = false;
        settings.SelectedCharacter = first;
        Answer(world, "Save your changes?", "PrimaryButton");
        WaitFor(() => settings.Status == AddonSettingsViewModel.SavedMessage && !settings.HasChanges);
        Assert.Equal(first, settings.SelectedCharacter);
        Assert.Null(AddonSettings.Load(file)["HoobiScriptsDB"].GetTable("settings")!.Get("hideErrors"));
        Assert.False(Row<ToggleSettingViewModel>(world, "hideErrors").IsOn);

        Row<ToggleSettingViewModel>(world, "hideErrors").IsOn = true;
        world.Navigate("Addons");
        world.Navigate("AddonSettings");
        Pump();
        Assert.True(settings.HasChanges);
        Assert.True(Row<ToggleSettingViewModel>(world, "hideErrors").IsOn);

        Click(world.Find<Button>(button => button.Name == "NewCharacterButton"));
        Answer(world, "Save your changes?", "CloseButton");
        Assert.Null(world.TryFind<FluentAvalonia.UI.Controls.FAContentDialog>(_ => true));
        Assert.True(settings.HasChanges);
    });

    [Fact]
    public void A_client_exit_rereads_the_files() => session.Run(() =>
    {
        var schema = SettingsSchema.Parse(File.ReadAllText(Fixture("hoobiscripts-settings-schema.json")));
        using var world = World(session, "addon-settings-client-exit", addon => addon.Id == "hoobiscripts" ? schema : null);
        var file = AddonSettings.FilePath(world.Beta, "1#1", "HoobiScripts");
        var settings = world.Main.AddonSettings;
        var install = world.Main.SelectedInstall!;
        Open(world);
        void GameWrites(bool hideErrors) => File.WriteAllText(file, File.ReadAllText(file).Replace($"[\"hideErrors\"] = {(!hideErrors).ToString().ToLowerInvariant()},", $"[\"hideErrors\"] = {hideErrors.ToString().ToLowerInvariant()},", StringComparison.Ordinal));
        void Play(Action whileRunning)
        {
            install.Client = new WowClientProcess(1, DateTimeOffset.Now);
            Pump();
            Assert.True(settings.IsClientRunning);
            whileRunning();
            install.Client = null;
            Pump();
        }

        Assert.True(Row<ToggleSettingViewModel>(world, "hideErrors").IsOn);
        Play(() => GameWrites(false));
        WaitFor(() => !Row<ToggleSettingViewModel>(world, "hideErrors").IsOn);

        Row<ToggleSettingViewModel>(world, "vendorOldFood").IsOn = true;
        Play(() => GameWrites(true));
        Answer(world, "Reload from the game?", "PrimaryButton");
        Assert.False(settings.HasChanges);
        Assert.False(Row<ToggleSettingViewModel>(world, "vendorOldFood").IsOn);
        Assert.True(Row<ToggleSettingViewModel>(world, "hideErrors").IsOn);

        Row<ToggleSettingViewModel>(world, "vendorOldFood").IsOn = true;
        Play(() => GameWrites(false));
        Answer(world, "Reload from the game?", "CloseButton");
        Assert.True(settings.HasChanges);
        Assert.True(Row<ToggleSettingViewModel>(world, "vendorOldFood").IsOn);
    });

    [Fact]
    public void A_forever_install_leaves_out_the_beta_realm_characters() => session.Run(() =>
    {
        var schema = SettingsSchema.Parse(File.ReadAllText(Fixture("hoobiscripts-settings-schema.json")));
        foreach (var product in new[] { ForeverLaunch.BetaProduct, ForeverLaunch.LiveProduct })
        {
            using var world = World(session, "addon-settings-" + product, addon => addon.Id == "hoobiscripts" ? schema : null, product: product);
            var file = AddonSettings.FilePath(world.Beta, "1#1", "HoobiScripts");
            var before = File.ReadAllText(file);
            var settings = world.Main.AddonSettings;
            Open(world);

            Assert.Equal(product, world.Main.SelectedInstall!.Install.ProductCode);
            var beta = product == ForeverLaunch.BetaProduct;
            Assert.Equal(beta ? 3 : 0, settings.Characters.Count);
            Assert.Equal(beta, settings.KnownRealms.ContainsKey(ForeverLaunch.BetaRealm));
            Assert.Equal(beta, settings.CanCreateCharacter);
            Assert.Equal(before, File.ReadAllText(file));
        }
    });

    [Fact]
    public void Rows_whose_file_the_game_has_not_written_are_read_only() => session.Run(() =>
    {
        var schema = SettingsSchema.Parse(File.ReadAllText(Fixture("hoobiscripts-settings-schema.json")));
        using var world = World(session, "addon-settings-no-file", addon => addon.Id == "hoobiscripts" ? schema : null, withFile: false);
        Open(world);

        var hide = Row<ToggleSettingViewModel>(world, "hideErrors");
        Assert.True(hide.IsReadOnly);
        Assert.False(hide.IsEnabled);
        Assert.Equal(AddonSettings.NotLoggedInNotice("Hoobi Scripts"), hide.Description);
        Assert.False(world.Main.AddonSettings.SaveCommand.CanExecute(null));
        Assert.False(File.Exists(AddonSettings.FilePath(world.Beta, "1#1", "HoobiScripts")));
    });

    [Fact]
    public void The_real_exported_schemas_render_every_row() => session.Run(() =>
    {
        using var world = new UiPathWorld(session, "addon-settings-real");
        var account = Path.Combine(world.Beta, "WTF", "Account", "1#1");
        File.Copy(Fixture("HoobiScripts.lua"), AddonSettings.FilePath(world.Beta, "1#1", "HoobiScripts"));
        Directory.CreateDirectory(Path.Combine(account, "91", "Hoobi-Quester"));
        SettingsSchema Real(string folder) => SettingsSchema.Parse(File.ReadAllText(Fixture(Path.Combine("settings-schemas", folder + ".json"))));
        var core = Real("HoobiScripts");
        var journey = Real("StewardJourney");
        world.Main.AddonSettings.FetchSchema = (addon, _, _) => Task.FromResult(addon.Id switch
        {
            "hoobiscripts" => core,
            "steward-journey" => journey,
            _ => (SettingsSchema?)null,
        });
        world.Main.AddonSettings.IsRunning = _ => false;

        world.Navigate("AddonSettings");
        WaitFor(() => world.Main.AddonSettings.Pages.Count > 0 && !world.Main.AddonSettings.IsLoading);
        world.Main.AddonSettings.SelectedCharacter = world.Main.AddonSettings.Characters.Single(option => option.Key.StartsWith("Hoobi Quester", StringComparison.Ordinal));
        Pump();

        var rows = world.Main.AddonSettings.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Rows).ToList();
        Assert.Equal(core.Pages!.Sum(page => page.Sections!.Sum(section => section.Rows!.Count)) + 1, rows.Count);
        Assert.DoesNotContain(rows, row => row.Description == SettingRowViewModel.UpdateStewardNotice);
        Assert.Contains(rows, row => row is ReadOnlySettingViewModel { ReadOnlyNotice: "Set in the game" });
        Assert.All(rows.Where(row => row.Row.IsCharacter), row => Assert.True(row.IsEnabled || row.IsReadOnly || row.Row.Parent is not null, row.Label));
        Assert.Equal(AddonSettings.NotLoggedInNotice("Steward: Journey"), rows.Single(row => row.Row.IsCharacterFile).Description);
        Assert.True(rows.Single(row => row.Row.Key == "actionBars").IsShown);
        Save(world, "addon-settings-real");

        var card = world.Find<SettingsCard>(found => found.Header as string == "Faster auto loot");
        var header = card.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Single(presenter => presenter.Name == "PART_Header");
        Assert.Equal(card.Padding.Left, header.TranslatePoint(default, card)!.Value.X, 1);
    });

    private static void Save(UiPathWorld world, string name)
    {
        var frame = world.Window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("STEWARD_SETTINGS_PNG_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            frame.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
        }

        TextCentring.Verify(world.Window, name);
    }
}
