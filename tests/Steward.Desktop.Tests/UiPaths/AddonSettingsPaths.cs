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

            Assert.True(Toggle("Log your leveling journey").IsEffectivelyEnabled);
            Flip("Log your leveling journey");
            Click(world.Find<Button>(button => button.Content as string == "Save"));
            WaitFor(() => world.Main.AddonSettings.Status is not null);
            var journeyFile = AddonSettings.CharacterFilePath(folder, "StewardJourney");
            Assert.False(AddonSettings.Load(journeyFile)["StewardJourneyDB"].GetTable("settings")!.Get("enabled")!.Boolean);
        });
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
        Save(world, "addon-settings-real");
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
