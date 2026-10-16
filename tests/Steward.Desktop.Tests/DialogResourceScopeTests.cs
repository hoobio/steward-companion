using Avalonia.Controls;

using FluentAvalonia.UI.Controls;

using Steward.Desktop.Services;

namespace Steward.Desktop.Tests;

public sealed class DialogResourceScopeTests(AvaloniaSession session)
{
    private static readonly string[] Titles = ["First", "Second"];

    [Fact]
    public void Every_dialog_shown_in_sequence_takes_the_dialog_brushes() => session.Run(() =>
    {
        var window = new Window { Width = 1280, Height = 820 };
        window.Show();
        foreach (var title in Titles)
        {
            var dialog = new FAContentDialog { Title = title, PrimaryButtonText = "OK", CloseButtonText = "Cancel" };
            var shown = AppDialogs.ShowAsync(dialog, window);
            AvaloniaSession.WaitFor(() => dialog.IsLoaded);

            Assert.True(dialog.Resources.ContainsKey("AccentButtonBackgroundPointerOver"));
            AvaloniaSession.CloseDialog(dialog, shown);
        }

        window.Close();
    });
}
