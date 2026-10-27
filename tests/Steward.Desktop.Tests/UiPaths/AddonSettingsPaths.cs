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
            var conflict = dialog.GetVisualDescendants().OfType<CheckBox>().Single();
            Assert.False(conflict.IsChecked);
            Save(world, "sync-import-dialog");

            Click(DialogButton(dialog, "PrimaryButton"));
            WaitFor(() => world.TryFind<FluentAvalonia.UI.Controls.FAContentDialog>(found => found != dialog && Text(found).Contains("Addon settings applied", StringComparison.Ordinal)) is not null);
            var done = world.Find<FluentAvalonia.UI.Controls.FAContentDialog>(found => Text(found).Contains("Addon settings applied", StringComparison.Ordinal));
            Assert.Contains("Applied 4 changes to 1#1", Text(done), StringComparison.Ordinal);
            Click(DialogButton(done, "CloseButton"));
            world.WaitForNoDialog();

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

    [Fact]
    public void A_link_that_arrives_while_the_result_dialog_is_open_is_shown_after_it_closes() => session.Run(() =>
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
        Press(world, WaitForDialogWith(world, "Apply changes from the website?"), "PrimaryButton");
        var result = WaitForDialogWith(world, "Addon settings applied");
        Assert.True(settings.IsBusy);

        var second = world.Main.ReceiveLinkAsync("steward://import#" + AddonSettingsPayload.Encode(Second));
        WaitFor(() => second.IsCompleted);
        Assert.Null(settings.Status);
        Assert.True(result.IsVisible);

        Press(world, result, "CloseButton");
        var confirmation = WaitForDialogWith(world, "Skin");
        Assert.True(settings.IsBusy);
        Assert.True(DialogButton(confirmation, "PrimaryButton").IsEffectivelyEnabled, Text(confirmation));
        Press(world, confirmation, "PrimaryButton");
        Press(world, WaitForDialogWith(world, "Addon settings applied"), "CloseButton");
        WaitFor(() => first.IsCompleted);

        Assert.True(first.IsCompletedSuccessfully);
        Assert.False(settings.IsBusy);
        Assert.True(settings.CanExport);
        Assert.Equal("hoobi", AddonSettings.Load(file)["HoobiScriptsDB"].GetTable("settings")!.GetString("skin"));
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

        var show = settings.ShowImport;
        settings.ShowImport = (_, _) => throw new InvalidOperationException("The dialog broke");
        var failed = world.Main.ReceiveLinkAsync(link);
        Settled(failed);
        Assert.Equal("The dialog broke", settings.Status);

        settings.ShowImport = (_, _) => throw new KeyNotFoundException("Unexpected");
        var unexpected = world.Main.ReceiveLinkAsync(link);
        Settled(unexpected);
        Assert.Equal("Unexpected", settings.Status);
        settings.ShowImport = show;

        var applied = world.Main.ReceiveLinkAsync(link);
        Press(world, WaitForDialogWith(world, "Apply changes from the website?"), "PrimaryButton");
        Press(world, WaitForDialogWith(world, "Addon settings applied"), "CloseButton");
        Settled(applied);
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
