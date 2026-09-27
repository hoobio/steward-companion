using Steward.App.ViewModels;

namespace Steward.App.Services;

public static class LiveUpdatesPreview
{
    public const string Argument = "--live-preview=reconnecting";

    public static bool IsRequested(IEnumerable<string> arguments) =>
        arguments.Contains(Argument, StringComparer.OrdinalIgnoreCase);

    public static void Apply(MainViewModel main)
    {
        ArgumentNullException.ThrowIfNull(main);

        main.UserName = "Hoobi";
        main.IsAuthorized = true;
        main.IsSignedIn = true;
        main.LiveUpdatesState = LiveUpdatesState.Reconnecting;
    }
}
