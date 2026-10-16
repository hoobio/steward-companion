using Windows.System;

namespace Steward.App.Services;

public static class WindowsShell
{
    private static readonly string RealDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steward");

    public static string DisplayDataFolder(string realDataFolder)
    {
        if (!AppInfo.IsPackaged)
        {
            return realDataFolder;
        }

        var relative = Path.GetRelativePath(RealDataRoot, realDataFolder);
        var virtualised = Path.Combine(Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path, "Local", "Steward", relative);
        return Directory.Exists(virtualised) ? virtualised : realDataFolder;
    }

    public static string? ProtocolActivationUri() =>
        AppInfo.IsPackaged && Windows.ApplicationModel.AppInstance.GetActivatedEventArgs() is Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs protocol
            ? protocol.Uri.AbsoluteUri
            : null;

    public static async Task LaunchUriAsync(Uri uri) => await Launcher.LaunchUriAsync(uri);

    public static async Task LaunchWithAppPickerAsync(Uri uri, nint ownerWindow)
    {
        var options = new LauncherOptions { DisplayApplicationPicker = true };
        WinRT.Interop.InitializeWithWindow.Initialize(options, ownerWindow);
        await Launcher.LaunchUriAsync(uri, options);
    }
}
