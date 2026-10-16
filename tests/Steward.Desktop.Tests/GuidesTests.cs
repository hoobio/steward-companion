using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Steward.App.Services;
using Steward.Desktop.Services;
using Steward.Desktop.Views;

namespace Steward.Desktop.Tests;

public sealed class GuidesTests(AvaloniaSession session)
{
    [Theory]
    [InlineData("current")]
    [InlineData("downloading")]
    [InlineData("written")]
    [InlineData("failed")]
    [InlineData("newer")]
    [InlineData("unfinished")]
    [InlineData("expired")]
    [InlineData("none")]
    [InlineData("loading")]
    public void Guides_page_renders_each_preview_scenario(string scenario) => Render(scenario, window =>
    {
        window.ShowGuidesPreview(scenario);
        Assert.True(window.FindControl<Button>("GuidesItem")!.IsVisible);
        return window.CurrentPage;
    }, page => Assert.IsType<GuidesPage>(page));

    [Theory]
    [InlineData("signin")]
    [InlineData("signin-error")]
    [InlineData("signin-busy")]
    [InlineData("mfa")]
    [InlineData("mfa-error")]
    [InlineData("mfa-busy")]
    public void Sign_in_dialog_renders_each_preview_scenario(string scenario)
    {
        Task? shown = null;
        Render(scenario, window =>
        {
            var dialog = new RestedXpSignInDialog(window.ViewModel.RestedXp);
            shown = AppDialogs.ShowAsync(dialog, window);
            AvaloniaSession.WaitFor(() => dialog.IsLoaded);
            return dialog;
        }, dialog =>
        {
            var box = Assert.IsType<RestedXpSignInDialog>(dialog);
            Assert.Equal(scenario.StartsWith("mfa", StringComparison.Ordinal), box.ViewModel.MfaVisibility);
            AvaloniaSession.CloseDialog(box, shown!);
        });
    }

    private void Render(string scenario, Func<MainWindow, object?> show, Action<object?> verify) => session.Run(() =>
    {
        using var host = session.NewHost();
        var window = host.Services.GetRequiredService<MainWindow>();
        GuidesPreview.Apply(window.ViewModel, scenario);
        window.Width = 1280;
        window.Height = 820;
        window.Show();
        var shown = show(window);
        window.Width = 1280;
        window.Height = 820;
        Dispatcher.UIThread.RunJobs();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("STEWARD_GUIDES_PNG") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            frame.Save(Path.Combine(directory, $"{scenario}.png"), PngBitmapEncoderOptions.Default);
        }

        TextCentring.Verify(window, shown is null ? $"guides {scenario}" : $"sign-in {scenario}");

        verify(shown);
        window.Hide();
    });
}
