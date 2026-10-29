using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

using Steward.App.Services;
using Steward.App.ViewModels;
using Steward.Desktop.Services;

namespace Steward.Desktop.Views;

public sealed partial class SettingsPage : UserControl
{
    public static readonly IValueConverter OnOff = new FuncValueConverter<bool, string>(on => on ? "On" : "Off");

    private const string NoTrayDescription = "Not available: no system tray is running on this desktop";

    private static readonly Color PulseColor = Color.FromArgb(0xFF, 0x2F, 0x5A, 0x85);

    private bool _editing;

    public SettingsPage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        CloseToTrayCard.IsVisible = !OperatingSystem.IsMacOS();
        if (OperatingSystem.IsMacOS())
        {
            MinimizeToTrayCard.Header = "Minimise to the menu bar";
            MinimizeToTrayCard.Description = "Minimising the window hides it to the menu bar instead of the Dock";
            AutomationProperties.SetName(MinimizeToTrayToggle, "Minimise to the menu bar");
        }

        var minimizeDescription = MinimizeToTrayCard.Description;
        var closeDescription = CloseToTrayCard.Description;

        AutoUpdateChoice.SelectedIndex = viewModel.AutoUpdateIndex;
        AutoUpdateChoice.SelectionChanged += (_, _) =>
        {
            if (AutoUpdateChoice.SelectedIndex >= 0)
            {
                viewModel.AutoUpdateIndex = AutoUpdateChoice.SelectedIndex;
            }
        };
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.AutoUpdateIndex))
            {
                AutoUpdateChoice.SelectedIndex = viewModel.AutoUpdateIndex;
            }
        };
        Loaded += (_, _) =>
        {
            var tray = TrayHost.IsAvailable;
            MinimizeToTrayCard.IsEnabled = CloseToTrayCard.IsEnabled = tray;
            MinimizeToTrayCard.Description = tray ? minimizeDescription : NoTrayDescription;
            CloseToTrayCard.Description = tray ? closeDescription : NoTrayDescription;
            AutoUpdateChoice.SelectedIndex = viewModel.AutoUpdateIndex;
            viewModel.AddonSettings.RefreshAccounts();
            viewModel.EditInstallRequested += OnEditInstallRequested;
            viewModel.AppUpdateFocusRequested += OnAppUpdateFocusRequested;
            _ = ShowPendingEditAsync();
            ShowPendingAppUpdateFocus();
        };
        Unloaded += (_, _) =>
        {
            viewModel.EditInstallRequested -= OnEditInstallRequested;
            viewModel.AppUpdateFocusRequested -= OnAppUpdateFocusRequested;
        };
    }

    public MainViewModel ViewModel { get; }

    private void OnAppUpdateFocusRequested(object? sender, EventArgs e) => ShowPendingAppUpdateFocus();

    private void ShowPendingAppUpdateFocus()
    {
        if (!ViewModel.TakeAppUpdateFocusRequest())
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            CenterAboutCard();
            _ = HighlightAboutCardAsync();
        });
    }

    private void CenterAboutCard()
    {
        if (AboutCard.TranslatePoint(default, Scroller) is not { } top)
        {
            return;
        }

        var target = Scroller.Offset.Y + top.Y - ((Scroller.Viewport.Height - AboutCard.Bounds.Height) / 2);
        var maximum = Math.Max(0, Scroller.Extent.Height - Scroller.Viewport.Height);
        Scroller.Offset = new Vector(Scroller.Offset.X, Math.Clamp(target, 0, maximum));
    }

    private async Task HighlightAboutCardAsync()
    {
        if (AboutCard.Background is not ISolidColorBrush original)
        {
            return;
        }

        var pulse = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(800),
            IterationCount = new IterationCount(2),
            PlaybackDirection = PlaybackDirection.Alternate,
            Easing = new CubicEaseInOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(BackgroundProperty, new SolidColorBrush(original.Color)) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(BackgroundProperty, new SolidColorBrush(PulseColor)) } },
            },
        };
        await pulse.RunAsync(AboutCard);
    }

    public void ShowInstalls() => Dispatcher.UIThread.Post(() => InstallsGroup.BringIntoView(), DispatcherPriority.Loaded);

    private void OnEditInstallRequested(object? sender, EventArgs e) => _ = ShowPendingEditAsync();

    private async Task ShowPendingEditAsync()
    {
        if (_editing || ViewModel.TakeEditInstallRequest() is not { } install || TopLevel.GetTopLevel(this) is not { } owner)
        {
            return;
        }

        _editing = true;
        try
        {
            await AppDialogs.ShowAsync(new EditInstallDialog(ViewModel, install), owner);
        }
        finally
        {
            _editing = false;
        }
    }

    private void OnEditInstallClick(object? sender, RoutedEventArgs e)
    {
        if (((Control)sender!).DataContext is WowInstallViewModel install)
        {
            ViewModel.RequestEditInstall(install);
        }
    }

    private void OnInstallsHeaderClick(object? sender, RoutedEventArgs e)
    {
        var expanded = !InstallsList.IsVisible;
        InstallsList.IsVisible = expanded;
        InstallsGroup.Classes.Set("expanded", expanded);
    }

    private static void Open(string target) => ShellOpen.Open(target);

    private void OnEditAddonSettingsClick(object? sender, RoutedEventArgs e) => ViewModel.AddonSettings.ExportCommand.Execute(null);

    private void OnGuildPanelClick(object? sender, RoutedEventArgs e) => Open("https://guild.hoobi.io");

    private void OnReleasesClick(object? sender, RoutedEventArgs e) =>
        Open("https://github.com/hoobio/steward-companion/releases");

    private void OnRepositoryClick(object? sender, RoutedEventArgs e) =>
        Open("https://github.com/hoobio/steward-companion");

    private void OnPrivacyClick(object? sender, RoutedEventArgs e) =>
        Open("https://github.com/hoobio/steward-companion/blob/main/PRIVACY.md");

    private void OnOpenDataFolderClick(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ViewModel.DataFolder);
        Open(WindowsShell.DisplayDataFolder(ViewModel.DataFolder));
    }

    private void OnOpenLogFolderClick(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ViewModel.LogFolder);
        Open(WindowsShell.DisplayDataFolder(ViewModel.LogFolder));
    }
}
