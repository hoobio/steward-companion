using Avalonia.Threading;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;

using Steward.Desktop.Services;
using Steward.Desktop.Views;

namespace Steward.Desktop.Tests;

public sealed class ForegroundGateTests(AvaloniaSession session)
{
    [Fact]
    public void A_gate_starts_released_and_holds_until_released()
    {
        var gate = new ForegroundGate();
        Assert.False(gate.IsHeld);
        Assert.False(gate.Release());

        gate.Hold();
        var waiting = gate.WhenReleased;
        Assert.True(gate.IsHeld);
        Assert.False(waiting.IsCompleted);

        Assert.True(gate.Release());
        Assert.True(waiting.IsCompleted);
        Assert.False(gate.IsHeld);
    }

    [Fact]
    public void A_dialog_waits_while_the_window_was_started_without_focus() => session.Run(() =>
    {
        using var host = session.NewHost();
        var window = host.Services.GetRequiredService<MainWindow>();
        window.ShowActivated = false;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.ForegroundGate.Hold();
        var dialog = new FAContentDialog { Title = "Title", CloseButtonText = "Close" };

        var shown = AppDialogs.ShowAsync(dialog, window);
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.False(dialog.IsLoaded);

            window.ForegroundGate.Release();
            AvaloniaSession.WaitFor(() => dialog.IsLoaded);
        }
        finally
        {
            window.ForegroundGate.Release();
            AvaloniaSession.CloseDialog(dialog, shown);
            window.Hide();
        }
    });
}
