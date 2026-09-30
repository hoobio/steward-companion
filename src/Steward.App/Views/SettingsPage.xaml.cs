using System.Diagnostics;

using Steward.App.Services;
using Steward.App.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

using Windows.UI;

namespace Steward.App.Views;

public sealed partial class SettingsPage : Page
{
    private bool _editing;

    public SettingsPage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (ViewModel is { } viewModel)
            {
                viewModel.EditInstallRequested += OnEditInstallRequested;
                viewModel.AppUpdateFocusRequested += OnAppUpdateFocusRequested;
                _ = ShowPendingEditAsync();
                ShowPendingAppUpdateFocus();
            }
        };
        Unloaded += (_, _) =>
        {
            if (ViewModel is { } viewModel)
            {
                viewModel.EditInstallRequested -= OnEditInstallRequested;
                viewModel.AppUpdateFocusRequested -= OnAppUpdateFocusRequested;
            }
        };
    }

    private void OnAppUpdateFocusRequested(object? sender, EventArgs e) => ShowPendingAppUpdateFocus();

    private void ShowPendingAppUpdateFocus()
    {
        if (ViewModel?.TakeAppUpdateFocusRequest() != true)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            AboutCard.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true, VerticalAlignmentRatio = 0.5 });
            HighlightAboutCard();
        });
    }

    private void HighlightAboutCard()
    {
        if (AboutCard.Background is not SolidColorBrush original)
        {
            return;
        }

        var brush = new SolidColorBrush(original.Color);
        AboutCard.Background = brush;
        var pulse = new ColorAnimation
        {
            To = Color.FromArgb(0xFF, 0x2F, 0x5A, 0x85),
            Duration = new Duration(TimeSpan.FromMilliseconds(800)),
            AutoReverse = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(pulse, brush);
        Storyboard.SetTargetProperty(pulse, "Color");
        var storyboard = new Storyboard();
        storyboard.Children.Add(pulse);
        storyboard.Completed += (_, _) => AboutCard.Background = original;
        storyboard.Begin();
    }

    public MainViewModel ViewModel { get; }

    private void OnEditInstallRequested(object? sender, EventArgs e) => _ = ShowPendingEditAsync();

    private async Task ShowPendingEditAsync()
    {
        if (_editing || ViewModel?.TakeEditInstallRequest() is not { } install)
        {
            return;
        }

        _editing = true;
        try
        {
            await AppDialogs.ShowAsync(new EditInstallDialog(ViewModel, install), XamlRoot);
        }
        finally
        {
            _editing = false;
        }
    }

    private void OnEditInstallClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is WowInstallViewModel install)
        {
            ViewModel?.RequestEditInstall(install);
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        Column.Width = Math.Min(e.NewSize.Width, Column.MaxWidth);

    private static void Open(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

    private void OnGuildPanelClick(object sender, RoutedEventArgs e) => Open("https://guild.hoobi.io");

    private void OnReleasesClick(object sender, RoutedEventArgs e) =>
        Open("https://github.com/hoobio/steward-companion/releases");

    private void OnRepositoryClick(object sender, RoutedEventArgs e) =>
        Open("https://github.com/hoobio/steward-companion");

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            Directory.CreateDirectory(viewModel.DataFolder);
            Open(App.DisplayDataFolder(viewModel.DataFolder));
        }
    }

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            Directory.CreateDirectory(viewModel.LogFolder);
            Open(App.DisplayDataFolder(viewModel.LogFolder));
        }
    }
}
