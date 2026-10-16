namespace Steward.App.Services;

public static class WindowsShell
{
    public static string DisplayDataFolder(string realDataFolder) => realDataFolder;

    public static string? ProtocolActivationUri() => null;

    public static Task LaunchUriAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ShellOpen.Open(uri.OriginalString);
        return Task.CompletedTask;
    }

    public static Task LaunchWithAppPickerAsync(Uri uri, nint ownerWindow) => LaunchUriAsync(uri);
}
