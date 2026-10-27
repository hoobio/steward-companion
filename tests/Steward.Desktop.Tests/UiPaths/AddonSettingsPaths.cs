using System.Text.Json.Nodes;

using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

using Steward.Core;
using Steward.Desktop.Views;

using static Steward.Desktop.Tests.UiPaths.UiPathWorld;

namespace Steward.Desktop.Tests.UiPaths;

public sealed class AddonSettingsPaths(AvaloniaSession session)
{
    private const string ImportJson = """
        {"v":1,"product":"wow_classic_beta","account":"1#1","basedOn":1761550000,
         "changes":[
           {"addon":"HoobiScripts","scope":"account","key":"hideErrors","from":true,"to":false},
           {"addon":"HoobiScripts","scope":"account","key":"autoRepair","from":"off","to":"own"},
           {"addon":"HoobiScripts","scope":"account","key":"skin","from":null,"to":"neon"},
           {"addon":"HoobiScripts","scope":"account","key":"mailTradeGoods","from":false,"to":true},
           {"addon":"HoobiScripts","scope":"character","character":"Zug Zug-Classic Beta PvP","key":"vendorPlainWater","from":null,"to":false}
         ],
         "newCharacters":[{"first":"zug","last":"zug","class":"WARRIOR","realm":"Classic Beta PvP"}],
         "plans":{"talents":[{"class":"DRUID","group":1,"from":null,"to":[1,2,3]}],"actionBars":[]}}
        """;

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static UiPathWorld World(AvaloniaSession session, string area)
    {
        var world = new UiPathWorld(session, area);
        File.Copy(Fixture("HoobiScripts.lua"), AddonSettings.FilePath(world.Beta, "1#1", "HoobiScripts"));
        Directory.CreateDirectory(Path.Combine(world.Beta, "WTF", "Account", "1#1", "91", "Hoobi-Spellbook"));
        var schema = SettingsSchema.Parse(File.ReadAllText(Fixture("hoobiscripts-settings-schema.json")));
        world.Main.AddonSettings.FetchSchema = (addon, _, _) => Task.FromResult(addon.Id == "hoobiscripts" ? schema : null);
        world.Main.AddonSettings.IsRunning = _ => false;
        world.Main.AddonSettings.Now = () => DateTimeOffset.FromUnixTimeSeconds(1761550000);
        return world;
    }

    private static SettingsCard Card(UiPathWorld world, string header) => world.Find<SettingsCard>(card => card.Header as string == header);

    private static JsonNode Exported(string url)
    {
        var fragment = url[(url.IndexOf('#', StringComparison.Ordinal) + 1)..];
        return JsonNode.Parse(AddonSettingsPayload.Decode(fragment))!;
    }

    [Fact]
    public void X7_X8_addon_settings_export_and_import() => session.Run(() =>
    {
        using var world = World(session, "addon-settings");
        var file = AddonSettings.FilePath(world.Beta, "1#1", "HoobiScripts");

        world.Step("X7", "Settings page exports an account's addon settings to the website", () =>
        {
            Assert.True(world.Main.AddonSettingsVisibility);
            world.Navigate("Settings");
            var group = world.ByName<StackPanel>("AddonSettingsGroup");
            group.BringIntoView();
            Pump();
            Assert.True(group.IsEffectivelyVisible);
            Assert.Equal("1#1", world.Main.AddonSettings.SelectedAccount?.Folder);
            Save(world, "sync-settings-card");

            var picker = world.ByName<ComboBox>("SettingsAccountPicker");
            picker.IsDropDownOpen = true;
            WaitFor(() => world.Roots.Count() > 1 || world.Window.OpenedPopups.Any());
            var list = Assert.IsType<Border>(picker.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single(popup => popup.Name == "PART_Popup").Child);
            WaitFor(() => Steward.Desktop.Styles.Acrylic.GetMode(list) == Steward.Desktop.Styles.AcrylicMode.Blur && list.Background is Avalonia.Media.ImageBrush);
            Save(world, "sync-account-picker");
            picker.IsDropDownOpen = false;
            Pump();

            Click(Card(world, "Edit on the website"));
            WaitFor(() => world.Opened.Count == 1);
            var url = world.Opened[0];
            Assert.StartsWith("https://guild.hoobi.io/addon-settings#v1.", url, StringComparison.Ordinal);
            var export = Exported(url);
            Assert.Equal(("wow_classic_beta", "1#1", 1761550000L), ((string?)export["product"], (string?)export["account"], (long)export["exportedAt"]!));
            Assert.True((bool)export["values"]!["HoobiScripts"]!["account"]!["hideErrors"]!);
            Assert.Contains("HoobiScripts", export["installed"]!.AsArray().Select(folder => (string?)folder));

            Click(Card(world, "Plan talents"));
            WaitFor(() => world.Opened.Count == 2);
            Assert.StartsWith("https://guild.hoobi.io/addon-settings?view=talents#v1.", world.Opened[1], StringComparison.Ordinal);
            Click(Card(world, "Plan action bars"));
            WaitFor(() => world.Opened.Count == 3);
            Assert.StartsWith("https://guild.hoobi.io/addon-settings?view=actionbars#v1.", world.Opened[2], StringComparison.Ordinal);
        });

        world.Step("X8", "Paste from website confirms each change and writes the account file", () =>
        {
            world.Main.AddonSettings.ReadClipboard = () => Task.FromResult<string?>("steward://import#" + AddonSettingsPayload.Encode(ImportJson));
            var paste = world.Find<Button>(button => button.Content as string == "Paste");
            paste.BringIntoView();
            Pump();
            Click(paste);
            WaitFor(() => world.TryFind<FluentAvalonia.UI.Controls.FAContentDialog>(_ => true) is not null || world.Main.AddonSettings.Status is not null);
            Assert.Null(world.Main.AddonSettings.Status);
            var dialog = world.WaitForDialog();
            var text = Text(dialog);
            Assert.Contains("Apply changes from the website?", text, StringComparison.Ordinal);
            Assert.Contains("Hide error messages", text, StringComparison.Ordinal);
            Assert.Contains("On → Off", text, StringComparison.Ordinal);
            Assert.Contains("Not applied: Not one of the choices", text, StringComparison.Ordinal);
            Assert.Contains("Zug Zug (Classic Beta PvP)", text, StringComparison.Ordinal);
            Assert.Contains("Druid talents, plan 1", text, StringComparison.Ordinal);
            Assert.Contains("1 point added to node 3", text, StringComparison.Ordinal);
            Assert.Contains("Already the same in the game: Mail trade goods", text, StringComparison.Ordinal);
            Assert.DoesNotContain("On → On", text, StringComparison.Ordinal);
            var conflict = dialog.GetVisualDescendants().OfType<CheckBox>().Single();
            Assert.False(conflict.IsChecked);
            Save(world, "sync-import-dialog");

            Click(DialogButton(dialog, "PrimaryButton"));
            world.WaitForNoDialog();
            var done = WaitForBanner(world, "Imported 4 changes to 1#1 on ");
            Assert.EndsWith("The game reads them at the next login.", done.Title, StringComparison.Ordinal);
            Assert.True(done.IsDismissible);
            var opened = world.Opened.Count;
            Assert.Single(done.Actions, action => action.Label == "Export again").Command.Execute(null);
            WaitFor(() => world.Opened.Count == opened + 1);
            Assert.StartsWith("https://guild.hoobi.io/addon-settings#v1.", world.Opened[^1], StringComparison.Ordinal);

            var globals = AddonSettings.Load(file);
            var settings = globals["HoobiScriptsDB"].GetTable("settings")!;
            Assert.Null(settings.Get("hideErrors"));
            Assert.Equal("all", settings.GetString("autoRepair"));
            Assert.Equal("blizzard", settings.GetString("skin"));
            var zug = globals["HoobiScriptsDB"].GetTable("chars")!.GetTable("Zug Zug-Classic Beta PvP")!;
            Assert.Equal("WARRIOR", zug.GetString("class"));
            Assert.False(zug.Get("vendorPlainWater")!.Boolean);
            Assert.True(Directory.Exists(Path.Combine(world.Beta, "WTF", "Account", "1#1", "91", "Zug-Zug")));
            Assert.Equal([1L, 2L, 3L], SettingsPlans.Talents(SettingsPlans.GetIndex(globals["HoobiScriptsDB"].GetTable("talentPlans")!.GetTable("DRUID"), 1)!));
        });
    });

    [Fact]
    public void An_import_link_for_an_unknown_account_or_a_running_game_writes_nothing() => session.Run(() =>
    {
        using var world = World(session, "addon-settings-refused");
        var file = AddonSettings.FilePath(world.Beta, "1#1", "HoobiScripts");
        var before = File.ReadAllText(file);

        var otherAccount = AddonSettingsPayload.Encode(ImportJson.Replace("\"1#1\"", "\"2#1\"", StringComparison.Ordinal));
        var unknown = world.Main.ReceiveLinkAsync("steward://import#" + Uri.EscapeDataString(otherAccount));
        WaitFor(() => unknown.IsCompleted);
        Assert.Equal("No World of Warcraft install here has the account 2#1", world.Main.AddonSettings.Status);

        world.Main.AddonSettings.IsRunning = _ => true;
        var running = world.Main.ReceiveLinkAsync("steward://import#" + AddonSettingsPayload.Encode(ImportJson));
        WaitFor(() => running.IsCompleted);
        Assert.Equal(AddonSettings.RunningMessage, world.Main.AddonSettings.Status);

        var garbage = world.Main.ReceiveLinkAsync("steward://import#v1.not-deflate");
        WaitFor(() => garbage.IsCompleted);
        Assert.Equal(AddonSettingsPayload.NotALink, world.Main.AddonSettings.Status);
        Assert.Null(world.TryFind<FluentAvalonia.UI.Controls.FAContentDialog>(_ => true));
        Assert.Equal(before, File.ReadAllText(file));
    });

    [Fact]
    public void The_export_is_refused_when_its_link_is_too_long() => session.Run(() =>
    {
        using var world = World(session, "addon-settings-too-long");
        var file = AddonSettings.FilePath(world.Beta, "1#1", "HoobiScripts");
        var random = new Random(7);
        var noise = string.Concat(Enumerable.Range(0, 3000).Select(i => $"[\"k{i}\"] = \"{Convert.ToHexString(BitConverter.GetBytes(random.NextInt64()))}\",\n"));
        File.WriteAllText(file, File.ReadAllText(file).Replace("[\"settings\"] = {", "[\"settings\"] = {\n" + noise, StringComparison.Ordinal));
        var json = File.ReadAllText(Fixture("hoobiscripts-settings-schema.json"));
        var rows = json.IndexOf("\"rows\": [", StringComparison.Ordinal) + "\"rows\": [".Length;
        var schema = SettingsSchema.Parse(json.Insert(rows, string.Concat(Enumerable.Range(0, 3000).Select(i => $"{{ \"type\": \"text\", \"key\": \"k{i}\", \"scope\": \"account\", \"path\": [\"HoobiScriptsDB\", \"settings\"], \"default\": \"\" }},"))));
        world.Main.AddonSettings.FetchSchema = (_, _, _) => Task.FromResult<SettingsSchema?>(schema);
        world.Main.AddonSettings.RefreshAccounts();

        var export = world.Main.AddonSettings.ExportCommand.ExecuteAsync(null);
        WaitFor(() => export.IsCompleted);

        Assert.Equal(AddonSettingsPayload.TooLarge, world.Main.AddonSettings.Status);
        Assert.Empty(world.Opened);
    });

    private static FluentAvalonia.UI.Controls.FAContentDialog WaitForDialogWith(UiPathWorld world, string text)
    {
        var dialog = world.WaitForDialog();
        Assert.Contains(text, Text(dialog), StringComparison.Ordinal);
        return dialog;
    }

    private static void Press(UiPathWorld world, FluentAvalonia.UI.Controls.FAContentDialog dialog, string button)
    {
        Click(DialogButton(dialog, button));
        WaitFor(() => world.TryFind<FluentAvalonia.UI.Controls.FAContentDialog>(found => found == dialog && found.IsVisible) is null);
    }

    private static Steward.App.ViewModels.BannerViewModel WaitForBanner(UiPathWorld world, string start)
    {
        WaitFor(() => world.Main.Banners.Any(banner => banner.Title.StartsWith(start, StringComparison.Ordinal)));
        return world.Main.Banners.First(banner => banner.Title.StartsWith(start, StringComparison.Ordinal));
    }

    [Fact]
    public void A_link_that_arrives_while_the_confirmation_is_open_is_shown_after_it_closes() => session.Run(() =>
    {
        using var world = World(session, "addon-settings-queued");
        var file = AddonSettings.FilePath(world.Beta, "1#1", "HoobiScripts");
        var settings = world.Main.AddonSettings;
        const string Second = """
            {"v":1,"product":"wow_classic_beta","account":"1#1","basedOn":1761550001,
             "changes":[{"addon":"HoobiScripts","scope":"account","key":"skin","from":"blizzard","to":"hoobi"}],
             "newCharacters":[],"plans":{"talents":[],"actionBars":[]}}
            """;

        var first = world.Main.ReceiveLinkAsync("steward://import#" + AddonSettingsPayload.Encode(ImportJson));
        var confirmation = WaitForDialogWith(world, "Apply changes from the website?");
        Assert.True(settings.IsBusy);

        var second = world.Main.ReceiveLinkAsync("steward://import#" + AddonSettingsPayload.Encode(Second));
        WaitFor(() => second.IsCompleted);
        Assert.Null(settings.Status);
        Assert.True(confirmation.IsVisible);

        Press(world, confirmation, "PrimaryButton");
        WaitForBanner(world, "Imported 4 changes to 1#1 on ");
        var queued = WaitForDialogWith(world, "Skin");
        Assert.True(settings.IsBusy);
        Assert.True(DialogButton(queued, "PrimaryButton").IsEffectivelyEnabled, Text(queued));
        Press(world, queued, "PrimaryButton");
        WaitForBanner(world, "Imported 1 change to 1#1 on ");
        WaitFor(() => first.IsCompleted);

        Assert.True(first.IsCompletedSuccessfully);
        Assert.False(settings.IsBusy);
        Assert.True(settings.CanExport);
        Assert.Equal("hoobi", AddonSettings.Load(file)["HoobiScriptsDB"].GetTable("settings")!.GetString("skin"));
    });

    [Fact]
    public void An_import_link_brings_the_window_out_of_the_tray() => session.Run(() =>
    {
        using var world = World(session, "addon-settings-tray");
        var settings = world.Main.AddonSettings;
        var link = "steward://import#" + AddonSettingsPayload.Encode(ImportJson);

        world.Window.HideToTray();
        Pump();
        Assert.False(world.Window.IsVisible);
        var first = world.Main.ReceiveLinkAsync(link);
        var confirmation = WaitForDialogWith(world, "Apply changes from the website?");
        Assert.True(world.Window.IsVisible);
        Assert.NotEqual(WindowState.Minimized, world.Window.WindowState);

        world.Window.WindowState = WindowState.Minimized;
        Pump();
        var second = world.Main.ReceiveLinkAsync(link);
        WaitFor(() => second.IsCompleted);
        Assert.NotEqual(WindowState.Minimized, world.Window.WindowState);
        Assert.True(world.Window.IsVisible);

        Press(world, confirmation, "CloseButton");
        Press(world, WaitForDialogWith(world, "Apply changes from the website?"), "CloseButton");
        WaitFor(() => first.IsCompleted);
        Assert.False(settings.IsBusy);
    });

    [Fact]
    public void An_import_link_during_a_slow_startup_waits_only_for_the_installs() => session.Run(() =>
    {
        using var world = World(session, "addon-settings-slow-start");
        var settings = world.Main.AddonSettings;
        Assert.True(world.Main.InstallsKnown.IsCompleted);
        var known = new TaskCompletionSource();
        settings.Initializing = () => new TaskCompletionSource().Task;
        settings.InstallsKnown = () => known.Task;

        var import = world.Main.ReceiveLinkAsync("steward://import#" + AddonSettingsPayload.Encode(ImportJson));
        Pump();
        Pump();
        Assert.False(import.IsCompleted);
        Assert.Null(world.TryFind<FluentAvalonia.UI.Controls.FAContentDialog>(_ => true));

        known.SetResult();
        Press(world, WaitForDialogWith(world, "Apply changes from the website?"), "CloseButton");
        WaitFor(() => import.IsCompleted);
        Assert.True(import.IsCompletedSuccessfully);
        Assert.False(settings.IsBusy);
    });

    private const string UnboundJson = """
        {"v":1,
         "changes":[{"addon":"HoobiScripts","scope":"account","key":"hideErrors","from":null,"to":false}],
         "newCharacters":[],
         "plans":{"talents":[{"class":"DRUID","group":1,"from":null,"to":[1,2]}],"actionBars":[],
                  "spells":[{"class":"DRUID","from":null,"to":{"Wrath":30,"Rejuvenation":-1}}]},
         "labels":{"nodes":{"1":"Ferocity"}}}
        """;

    [Fact]
    public void An_unbound_import_asks_for_the_install_and_account_and_writes_only_plans() => session.Run(() =>
    {
        using var world = World(session, "addon-settings-unbound");
        var settings = world.Main.AddonSettings;
        var first = AddonSettings.FilePath(world.Beta, "1#1", "HoobiScripts");
        var second = AddonSettings.FilePath(world.Beta, "2#1", "HoobiScripts");
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        File.WriteAllText(second, File.ReadAllText(first).Replace("HoobiScriptsDB = {", "HoobiScriptsDB = {\n[\"spellPlans\"] = {\n[\"DRUID\"] = {\n[\"Wrath\"] = 22,\n},\n},", StringComparison.Ordinal));
        var firstBefore = File.ReadAllText(first);
        Steward.App.ViewModels.SettingsImportSession? shown = null;
        var show = settings.ShowImport!;
        settings.ShowImport = found =>
        {
            shown = found;
            return show(found);
        };

        var import = world.Main.ReceiveLinkAsync("steward://import#" + AddonSettingsPayload.Encode(UnboundJson));
        var dialog = WaitForDialogWith(world, "Wrath: not learning → up to level 30");
        Assert.NotNull(shown);
        Assert.True(shown.IsUnbound);
        Assert.Same(world.Main.SelectedInstall, shown.SelectedInstall);
        Assert.Equal(["1#1", "2#1"], shown.Accounts.Select(account => account.Folder));
        Assert.Equal("1#1", shown.SelectedAccount?.Folder);
        var text = Text(dialog);
        Assert.Contains("Not applied: " + AddonSettingsImport.UnboundRefusal, text, StringComparison.Ordinal);
        Assert.Contains("Druid spell training", text, StringComparison.Ordinal);
        Assert.Contains("No plan → 2 spells", text, StringComparison.Ordinal);
        Assert.Contains("1 point added to Ferocity", text, StringComparison.Ordinal);
        Assert.Contains("1 point added to node 2", text, StringComparison.Ordinal);
        Assert.Empty(dialog.GetVisualDescendants().OfType<CheckBox>());

        var accounts = dialog.GetVisualDescendants().OfType<ComboBox>().Single(box => Avalonia.Automation.AutomationProperties.GetName(box) == "WoW account");
        var installs = dialog.GetVisualDescendants().OfType<ComboBox>().Single(box => Avalonia.Automation.AutomationProperties.GetName(box) == "World of Warcraft install");
        Assert.Equal(world.Main.SelectedInstall!.Label, installs.SelectedItem as string);
        Assert.Equal(0, accounts.SelectedIndex);
        accounts.SelectedIndex = 1;
        WaitFor(() => Text(dialog).Contains("Wrath: up to level 22 → up to level 30", StringComparison.Ordinal));
        Assert.Equal("2#1", shown.Current?.Account);
        Assert.Contains("1 spell → 2 spells", Text(dialog), StringComparison.Ordinal);
        Assert.True(DialogButton(dialog, "PrimaryButton").IsEffectivelyEnabled);
        Save(world, "unbound-dialog");

        accounts.IsDropDownOpen = true;
        var list = Assert.IsType<Border>(accounts.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single(popup => popup.Name == "PART_Popup").Child);
        WaitFor(() => Steward.Desktop.Styles.Acrylic.GetMode(list) == Steward.Desktop.Styles.AcrylicMode.Blur && list.Background is Avalonia.Media.ImageBrush);
        Save(world, "unbound-dialog-account-picker");
        accounts.IsDropDownOpen = false;
        Pump();
        Assert.Equal("2#1", shown.Current?.Account);

        Press(world, dialog, "PrimaryButton");
        WaitFor(() => import.IsCompleted);
        Assert.True(import.IsCompletedSuccessfully);
        WaitForBanner(world, "Imported 2 changes to 2#1 on ");
        Assert.Equal(firstBefore, File.ReadAllText(first));
        var db = AddonSettings.Load(second)["HoobiScriptsDB"];
        Assert.Equal(30, db.GetTable("spellPlans")!.GetTable("DRUID")!.GetNumber("Wrath"));
        Assert.Equal(-1, db.GetTable("spellPlans")!.GetTable("DRUID")!.GetNumber("Rejuvenation"));
        Assert.Equal([1L, 2L], SettingsPlans.Talents(SettingsPlans.GetIndex(db.GetTable("talentPlans")!.GetTable("DRUID"), 1)!));
        Assert.True(db.GetTable("settings")!.Get("hideErrors")!.Boolean);
    });

    [Fact]
    public void The_busy_state_clears_after_every_outcome() => session.Run(() =>
    {
        using var world = World(session, "addon-settings-busy");
        var settings = world.Main.AddonSettings;
        var link = "steward://import#" + AddonSettingsPayload.Encode(ImportJson);
        void Settled(Task import)
        {
            WaitFor(() => import.IsCompleted);
            Assert.True(import.IsCompletedSuccessfully);
            Assert.False(settings.IsBusy);
            Assert.True(settings.CanExport);
            Assert.True(settings.PasteCommand.CanExecute(null));
        }

        var cancelled = world.Main.ReceiveLinkAsync(link);
        Press(world, WaitForDialogWith(world, "Apply changes from the website?"), "CloseButton");
        Settled(cancelled);
        Assert.Null(settings.Status);

        var refused = world.Main.ReceiveLinkAsync("steward://import#" + AddonSettingsPayload.Encode(ImportJson.Replace("\"1#1\"", "\"9#9\"", StringComparison.Ordinal)));
        Settled(refused);
        Assert.Equal("No World of Warcraft install here has the account 9#9", settings.Status);
        Assert.Equal("CautionTintBrush", WaitForBanner(world, "No World of Warcraft install here has the account 9#9").Background);

        var show = settings.ShowImport;
        settings.ShowImport = _ => throw new InvalidOperationException("The dialog broke");
        var failed = world.Main.ReceiveLinkAsync(link);
        Settled(failed);
        Assert.Equal("The dialog broke", settings.Status);
        Assert.Equal("CriticalTintBrush", WaitForBanner(world, "The dialog broke").Background);

        settings.ShowImport = _ => throw new KeyNotFoundException("Unexpected");
        var unexpected = world.Main.ReceiveLinkAsync(link);
        Settled(unexpected);
        Assert.Equal("Unexpected", settings.Status);
        settings.ShowImport = show;

        var applied = world.Main.ReceiveLinkAsync(link);
        Press(world, WaitForDialogWith(world, "Apply changes from the website?"), "PrimaryButton");
        Settled(applied);
        WaitForBanner(world, "Imported 4 changes to 1#1 on ");
        Assert.Null(settings.Status);
    });

    [Fact]
    public void Clickable_settings_cards_are_buttons_that_automation_can_press() => session.Run(() =>
    {
        using var world = World(session, "settings-card-automation");
        world.Navigate("Settings");
        Pump();

        static IEnumerable<AutomationPeer> Descendants(AutomationPeer peer) => peer.GetChildren().SelectMany(child => Descendants(child).Prepend(child));
        var tree = Descendants(ControlAutomationPeer.CreatePeerForElement(world.Window)).Where(peer => peer.IsControlElement()).ToList();
        AutomationPeer Button(string name) => Assert.Single(tree, peer => peer.GetName() == name && peer.GetAutomationControlType() == AutomationControlType.Button);
        var clickable = world.Everything.OfType<SettingsCard>().Where(card => card.IsClickEnabled).ToList();
        Assert.Contains(clickable, card => card.Header as string == "Privacy policy");
        Assert.Contains(clickable, card => card.Header as string == "Edit on the website");
        foreach (var card in clickable)
        {
            Assert.NotNull(Button((string)card.Header!).GetProvider<IInvokeProvider>());
        }

        Assert.Null(tree.Single(peer => peer.GetName() == "App data" && peer.GetAutomationControlType() == AutomationControlType.Group).GetProvider<IInvokeProvider>());

        Button("Privacy policy").GetProvider<IInvokeProvider>()!.Invoke();
        Assert.Contains("https://github.com/hoobio/steward-companion/blob/main/PRIVACY.md", world.Opened);
        Button("Plan talents").GetProvider<IInvokeProvider>()!.Invoke();
        WaitFor(() => world.Opened.Any(url => url.StartsWith("https://guild.hoobi.io/addon-settings?view=talents#v1.", StringComparison.Ordinal)));

        var late = new SettingsCard { Header = "Late" };
        var peer = ControlAutomationPeer.CreatePeerForElement(late);
        Assert.Null(peer.GetProvider<IInvokeProvider>());
        late.IsClickEnabled = true;
        var clicks = 0;
        late.Click += (_, _) => clicks++;
        Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
        peer.GetProvider<IInvokeProvider>()!.Invoke();
        Assert.Equal(1, clicks);
    });

    private static void Save(UiPathWorld world, string name)
    {
        Pump();
        if (Environment.GetEnvironmentVariable("STEWARD_SETTINGS_PNG_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            using var frame = world.Window.CaptureRenderedFrame()!;
            using var target = new RenderTargetBitmap(frame.PixelSize, frame.Dpi);
            using (var context = target.CreateDrawingContext())
            {
                context.DrawImage(frame, new Rect(frame.Size));
                foreach (var popup in world.Roots.Skip(1))
                {
                    using var image = popup.CaptureRenderedFrame();
                    if (image is not null)
                    {
                        context.DrawImage(image, new Rect(world.Window.PointToClient(popup.PointToScreen(default)), image.Size));
                    }
                }
            }

            target.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
        }

        TextCentring.Verify(world.Window, name);
    }
}
