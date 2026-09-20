using Steward.App.ViewModels;
using Steward.Core;

namespace Steward.App.Services;

public static class GuidesPreview
{
    public const string Argument = "--guides-preview=";

    private const string Email = "ayian@outlook.com";
    private const string BattleTag = "Hoobi#11438";
    private const string Forever = "Forever Leveling Guide - Both Factions";

    private static readonly (string Product, TimeSpan Age)[] Catalogue =
    [
        (Forever, TimeSpan.FromDays(2)),
        ("Mists of Pandaria Guide - Bundle", TimeSpan.FromDays(21)),
        ("Cataclysm Alliance Leveling Guide 1-85", TimeSpan.FromDays(150)),
    ];

    private static readonly WowInstall Install = new(
        @"C:\Program Files (x86)\World of Warcraft",
        "_classic_beta_",
        @"C:\Program Files (x86)\World of Warcraft\_classic_beta_",
        @"C:\Program Files (x86)\World of Warcraft\_classic_beta_\Interface\AddOns",
        "wow_classic_beta",
        "4.4.2.61618",
        "World of Warcraft: Forever - Beta");

    public static string? Scenario(IEnumerable<string> arguments) => arguments
        ?.FirstOrDefault(argument => argument.StartsWith(Argument, StringComparison.OrdinalIgnoreCase))
        ?.Split('=', 2)[1];

    public static bool OpensDialog(string scenario) =>
        scenario is not null && (scenario.StartsWith("signin", StringComparison.Ordinal) || scenario.StartsWith("mfa", StringComparison.Ordinal));

    public static void Apply(MainViewModel main, string scenario)
    {
        ArgumentNullException.ThrowIfNull(main);

        main.IsGuidesPreview = true;
        main.UserName = "Hoobi";
        main.Role = "global";
        main.IsAuthorized = true;
        main.IsSignedIn = true;

        var products = scenario == "none" ? [] : Catalogue;
        var timestamps = products.ToDictionary(
            entry => entry.Product,
            entry => DateTimeOffset.Now.Subtract(entry.Age).ToUnixTimeMilliseconds(),
            StringComparer.Ordinal);

        var card = new RestedXpInstallViewModel(Install, _ => Task.CompletedTask)
        {
            IsClientRunning = scenario == "waiting",
            AddonVersion = "v4.11.4",
            IsSessionActive = scenario != "expired",
        };
        card.SetProducts([.. products.Select(entry => entry.Product)], Forever, timestamps);

        var signedIn = !OpensDialog(scenario) && scenario != "expired";
        main.RestedXp.ApplyPreview(Email, BattleTag, card, signedIn, scenario == "expired");
        if (card.SelectedRow is { } row)
        {
            row.State = State(scenario);
        }

        if (OpensDialog(scenario))
        {
            SeedDialog(main.RestedXp, scenario);
        }
    }

    private static void SeedDialog(RestedXpViewModel restedXp, string scenario)
    {
        restedXp.IsMfaRequired = scenario.StartsWith("mfa", StringComparison.Ordinal);
        restedXp.UsernameInput = scenario == "signin" ? string.Empty : Email;
        restedXp.PasswordInput = scenario is "signin-error" or "signin-busy" ? "hunter2hunt" : string.Empty;
        restedXp.MfaCode = scenario is "mfa-error" or "mfa-busy" ? "418203" : string.Empty;
        restedXp.ErrorMessage = scenario switch
        {
            "signin-error" => "Wrong username or password",
            "mfa-error" => "That code was not accepted",
            _ => null,
        };
        restedXp.IsBusy = scenario is "signin-busy" or "mfa-busy";
    }

    private static GuideRowState State(string scenario) => scenario switch
    {
        "downloading" => GuideRowState.Downloading,
        "waiting" => GuideRowState.Waiting,
        "written" => GuideRowState.Written,
        "failed" => GuideRowState.Failed,
        "newer" => GuideRowState.NeedsNewerAddon,
        "expired" => GuideRowState.None,
        _ => GuideRowState.InGame,
    };
}
