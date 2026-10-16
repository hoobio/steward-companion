using Steward.App.ViewModels;

namespace Steward.App.Services;

public static class ClientOutdatedPreview
{
    public const string Argument = "--client-outdated-preview";

    public static bool IsRequested(IEnumerable<string> arguments) =>
        arguments.Contains(Argument, StringComparer.OrdinalIgnoreCase);

    public static void Apply(MainViewModel main)
    {
        ArgumentNullException.ThrowIfNull(main);

        main.UserName = "Hoobi";
        main.IsAuthorized = true;
        main.IsSignedIn = true;
        main.ReportClientOutdated("This version of Steward no longer works with the guild API. Update to 0.13.0 or newer.");
        main.RescanCommand.Execute(null);
    }
}
