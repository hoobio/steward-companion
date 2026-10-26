using System.Collections.Concurrent;
using System.Globalization;

namespace Steward.Desktop.Tests.UiPaths;

public static class UiPathLedger
{
    private const string OsFrame = "drawn by the OS or the window manager outside the client area; the headless platform has no window manager";
    private const string OsTray = "the OS tray icon and its native menu live in the shell, outside the app's visual tree";
    private const string X11Compositor = "native X11 popup windows under a compositor; the headless platform renders popups without an X server";
    private const string OsHandler = "queries or writes the OS default-app association (xdg-mime, Windows associations)";
    private const string Process = "a second process launch or a process exit; the suite runs in one test process";
    private const string ReleaseOnly = "the Start on login card is enabled only in a GitHub release build (AppInfo.IsGitHubRelease, read from the entry assembly) and writes the real HKCU Run key, autostart entry or LaunchAgent; path 56 asserts the disabled card";
    private const string Repeat ="a repeat of the same path after an earlier run in the ledger session; the suite drives that path once per area";

    private static readonly ConcurrentDictionary<string, string> Results = new(StringComparer.Ordinal);

    public static readonly IReadOnlyList<(string Id, string Name, string? Skip)> Paths =
    [
        ("00", "start signed in", null),
        ("01", "default and 1280x820 window size", null),
        ("02", "active, deactivated and reactivated caption glyphs", OsFrame),
        ("03", "close caption button hover red", OsFrame),
        ("04", "maximise caption button hover", OsFrame),
        ("05", "double-click title bar maximises and restores", OsFrame),
        ("06", "title bar drag moves the window", OsFrame),
        ("07", "resize from the bottom-right grip", OsFrame),
        ("08", "Updates tab reads No addons match", null),
        ("09", "filter bug shows BugSack and BugGrabber, then clears", null),
        ("10", "BugSack overflow menu items", null),
        ("11", "menu item hover", null),
        ("12", "Escape closes the menu", null),
        ("13", "Steward overflow menu (auto-install)", null),
        ("14", "second click on the More button closes the menu", null),
        ("15", "Uninstall BugSack confirm dialog", null),
        ("16", "Tab cycles inside the dialog", null),
        ("17", "Escape closes the dialog", null),
        ("18", "Cancel closes the dialog and removes nothing", null),
        ("19", "fresh dialog then Escape", null),
        ("20", "no path 20 in the ledger", "the ledger has no path 20"),
        ("21", "Release channel dialog from the row's channel pill", null),
        ("22", "Pre-release selected, primary reads Use pre-release", null),
        ("23", "inline changelog in the channel dialog, Escape closes without saving", null),
        ("24", "Steward changelog dialog with sub-addon sections", null),
        ("25", "Enter closes the changelog", null),
        ("26", "Escape closes the BugSack changelog", null),
        ("27", "BugGrabber changelog, Escape closes", null),
        ("28", "channel dialog re-show and Escape", null),
        ("29", "channel dialog from the menu and Escape", null),
        ("30", "uninstall dialog re-show after other dialogs and Escape", null),
        ("31", "channel dialog Tab order", null),
        ("32", "Steward folders dialog with the Released tooltip", null),
        ("33", "a click through the Released tooltip opens the changelog", null),
        ("34", "folders dialog re-shown after the changelog keeps Tab inside", null),
        ("35", "Escape closes the re-shown folders dialog", null),
        ("36", "Get addons opens with the search box focused", null),
        ("37", "popular results load", null),
        ("38", "typing before any click goes into the search box", null),
        ("39", "sources ComboBox opens", null),
        ("40", "source CurseForge filters and a new search resets the scroll", null),
        ("41", "Escape closes Get addons", null),
        ("42", "install picker flyout", null),
        ("43", "picker row hover with the path tooltip", null),
        ("44", "pencil hover Edit install tooltip and menu item hover", null),
        ("45", "second click on the picker closes the flyout", null),
        ("46", "pencil opens Edit install on Settings, Escape closes", null),
        ("47", "picker reopens, an outside click closes it", null),
        ("48", "Open AddOns folder opens the install's AddOns path", null),
        ("49", "Copy from the beta wizard", null),
        ("50", "Escape closes the wizard, the banner stays, nothing recorded", null),
        ("51", "Sync page collapsed", null),
        ("52", "Your characters expanded", null),
        ("53", "Your characters collapsed again", null),
        ("54", "Write again writes StewardSync.lua", null),
        ("55", "Sync page Tab order", null),
        ("56", "Settings top", null),
        ("57", "Settings from the nav with its tooltip", null),
        ("58", "Settings bottom, About card", null),
        ("59", "Start on login on writes the entry", ReleaseOnly),
        ("60", "Start on login off removes the entry", ReleaseOnly),
        ("61", "segmented control hover", null),
        ("62", "one wheel notch scrolls Settings", null),
        ("63", "CurseForge off persists to state", null),
        ("64", "Addons with CurseForge off hides its rows and Get addons", null),
        ("65", "Hidden segment lists the hidden rows", null),
        ("66", "CurseForge on again restores the rows", null),
        ("67", "Installs card collapses", null),
        ("68", "Installs card reopens", null),
        ("69", "Edit install dialog focuses the name box", null),
        ("70", "Edit install Tab order", null),
        ("71", "Escape closes Edit install, nothing saved", null),
        ("72", "Check for updates", null),
        ("73", "close confirmation", null),
        ("74", "Minimize to tray hides the window", null),
        ("75", "tray tooltip", OsTray),
        ("76", "tray menu items", OsTray),
        ("77", "tray Open Steward restores the window", OsTray),
        ("78", "a minimise hides to the tray", null),
        ("79", "tray click restores the window", OsTray),
        ("80", "account flyout", null),
        ("81", "one guild: the guild row is not interactive", null),
        ("82", "account menu item hover", null),
        ("83", "Escape closes the account flyout", null),
        ("84", "second click closes the account flyout, the tooltip leads with the name", null),
        ("85", "nav item hover and tooltip", null),
        ("86", "subtle button hover (Get addons)", null),
        ("87", "segmented tab hover", null),
        ("88", "table row has no hover fill", null),
        ("89", "caption minimise and close hover", OsFrame),
        ("90", "refresh hover with its tooltip", null),
        ("91", "row menu item hover after a tray restore", null),
        ("92", "picker row hover", null),
        ("93", "version tooltip", null),
        ("94", "released tooltip with a date", null),
        ("95", "single-channel tooltip", null),
        ("96", "status tooltip Up to date", null),
        ("97", "lock notice tooltip", null),
        ("98", "More tooltip", null),
        ("99", "refresh tooltip", null),
        ("100", "RestedXP sign-in dialog", null),
        ("101", "Escape closes the sign-in dialog", null),
        ("102", "Escape after typing in the password box closes it", null),
        ("103", "tray cards follow the tray capability", null),
        ("104", "close confirmation offers Minimize without a tray", null),
        ("105", "Minimize iconifies (X11 WM_STATE)", OsFrame),
        ("106", "restored by windowactivate", OsFrame),
        ("107", "a tray host started later registers the icon", OsTray),
        ("108", "a curseforge link asks to install DBM", null),
        ("109", "Cancel installs nothing", null),
        ("110", "banners preview", null),
        ("111", "update progress in the InfoBar and the About card", null),
        ("112", "client outdated error bar", null),
        ("113", "Install World of Warcraft: Forever banner", null),
        ("114", "guides preview none", null),
        ("115", "guides preview expired", null),
        ("116", "guides preview current", null),
        ("117", "guides preview downloading", null),
        ("118", "guides preview written", null),
        ("119", "guides preview failed", null),
        ("120", "guides preview newer", null),
        ("121", "guides preview unfinished", null),
        ("122", "guides preview ingame", null),
        ("123", "guides preview signin", null),
        ("124", "guides preview signin-error", null),
        ("125", "guides preview signin-busy", null),
        ("126", "guides preview mfa", null),
        ("127", "guides preview mfa-error", null),
        ("128", "guides preview mfa-busy", null),
        ("129", "start with a compositor", X11Compositor),
        ("130", "native install picker popup", X11Compositor),
        ("131", "native row menu after deactivate and reactivate", X11Compositor),
        ("132", "native account flyout", X11Compositor),
        ("133", "Edit install game version list", null),
        ("134", "Escape twice closes the list and the dialog", null),
        ("135", "Open CurseForge links with Steward banner", OsHandler),
        ("136", "the dismissed default-handler banner stays away", OsHandler),
        ("137", "Set as default writes the scheme handler", OsHandler),
        ("138", "started with the API unreachable", null),
        ("139", "tray menu before quit", OsTray),
        ("140", "tray Quit ends the process", Process),
        ("141", "a normal launch signs in again", null),
        ("142", "Add install opens the OS folder chooser", "the OS folder chooser is a native dialog; the headless storage provider cannot pick a folder"),
        ("143", "Cancel in the folder chooser adds nothing", "the OS folder chooser is a native dialog; the headless storage provider cannot pick a folder"),
        ("144", "Manage installs opens Settings with Installs expanded", null),
        ("145", "tray Check for updates", OsTray),
        ("146", "signed-out gate", null),
        ("147", "Sign in with Discord with the browser failing", "binds a real loopback listener and launches the OS browser"),
        ("152", "three wheel notches add up to three notch distances", null),
        ("155", "--tray start and a second --tray launch", Process),
        ("156", "a plain second launch shows the running window", Process),
        ("157", "panel task list shows the icon and title", OsFrame),
        ("211", "restart", Process),
        ("212", "the first click on the row menu after a start opens it", null),
        ("230", "Settings bottom again", Repeat),
        ("231", "picker open again", Repeat),
        ("232", "Manage installs again from the bottom of Settings", Repeat),
        ("241", "Edit install with labels", Repeat),
        ("260", "final restored state", Repeat),
        ("X1", "guides loading row", null),
        ("X2", "folders dialog re-shown five times, each through the Released tooltip", null),
        ("X3", "Get addons search scroll resets", null),
        ("X4", "tooltip-then-click three times in the table", null),
        ("X5", "launch countdown under the install picker and its timers in the picker menu", null),
        ("X6", "a search for elv puts TukUI's ElvUI first", null),
    ];

    private static readonly string? ResultsFile = Environment.GetEnvironmentVariable("STEWARD_UIPATHS_PNG_DIR") is { Length: > 0 } directory
        ? Path.Combine(Directory.CreateDirectory(directory).FullName, "results.txt")
        : null;

    public static string Id(int id) => id.ToString("00", CultureInfo.InvariantCulture);

    public static bool IsDriven(string id) => Paths.Any(path => path.Id == id && path.Skip is null);

    public static void Record(string id, string status)
    {
        Results[id] = status;
        Append($"{id}\t{status}\t{Paths.First(path => path.Id == id).Name}");
    }

    public static void Unrouted(string area, IEnumerable<string> requests)
    {
        foreach (var request in requests.Distinct(StringComparer.Ordinal))
        {
            Append($"unrouted\t{area}\t{request}");
        }
    }

    private static void Append(string line)
    {
        if (ResultsFile is not null)
        {
            lock (Results)
            {
                File.AppendAllText(ResultsFile, line + Environment.NewLine);
            }
        }
    }
}
