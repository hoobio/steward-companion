namespace Steward.App.Services;

public static class PopupsPreview
{
    public const string Argument = "--popups-preview=";

    public static string? Scenario(IEnumerable<string> arguments) => arguments
        ?.FirstOrDefault(argument => argument.StartsWith(Argument, StringComparison.OrdinalIgnoreCase))
        ?.Split('=', 2)[1];
}
