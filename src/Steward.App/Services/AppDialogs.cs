using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Steward.App.Services;

internal static class AppDialogs
{
    private static Task _current = Task.CompletedTask;

    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog, XamlRoot root)
    {
        var previous = _current;
        var done = new TaskCompletionSource();
        _current = done.Task;
        try
        {
            // Only one ContentDialog can be open per window; a second ShowAsync throws, so a link arriving mid-dialog waits.
            await previous.ConfigureAwait(true);
            dialog.Style = (Style)Application.Current.Resources["StewardDialogStyle"];
            // The app's ButtonBackgroundPointerOver matches ContentDialogBackground, so a hovered Cancel would vanish into the dialog.
            dialog.Resources["ButtonBackgroundPointerOver"] = Application.Current.Resources["DialogButtonBackgroundPointerOver"];
            dialog.Resources["ButtonBackgroundPressed"] = Application.Current.Resources["DialogButtonBackgroundPressed"];
            dialog.XamlRoot = root;
            FlyoutOpener.HideAll(root);
            return await dialog.ShowAsync();
        }
        finally
        {
            done.SetResult();
        }
    }
}
