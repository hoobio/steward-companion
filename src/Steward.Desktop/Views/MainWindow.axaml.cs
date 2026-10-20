using System.ComponentModel;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Steward.App;
using Steward.App.Services;
using Steward.App.ViewModels;
using Steward.Core;
using Steward.Core.Diagnostics;
using Steward.Desktop.Services;

namespace Steward.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private const double InstallPickerGap = 8;
    private const double AccountFlyoutBottomGap = 8;
    private const double RolePillGap = 8;

    private readonly IServiceProvider _services;
    private readonly ILogger<MainWindow> _logger;
    private readonly DispatcherTimer _submenuOpen = new();
    private readonly DispatcherTimer _submenuClose = new();
    private Button? _selectedNav;
    private bool _quitting;
    private bool _confirmingClose;
    private bool _hideWhenMinimized;
    private bool _shown;
    private bool _pageShown;
    private DispatcherTimer? _minimizeHideTimer;
    private DispatcherTimer? _trimTimer;

    public MainWindow(MainViewModel viewModel, IServiceProvider services, ILogger<MainWindow> logger)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _services = services;
        _logger = logger;
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // Under Wine 11 on xvfb the extended client area gave a 980x690 X window with the title bar row clipped off the top.
        ExtendClientAreaToDecorationsHint = !AppInfo.IsWine;
        // WinUI keeps the 1px top resize border outside its client area, where Avalonia's extended client area draws over it.
        Root.Margin = AppInfo.IsWine ? default : new Thickness(0, 1, 0, 0);
        Title = AppInfo.WindowTitle;
        TitleText.Text = AppInfo.WindowTitle;
        TitleArea.IsVisible = !AppInfo.IsWine;
        ConfigurePlatformWindow();
        services.GetRequiredService<AvaloniaPlatform>().Owner = this;

        ViewModel.NavigateToAddons = () => Navigate(AddonsItem);
        ViewModel.NavigateToPageTag = tag => Navigate(tag switch
        {
            "sync" => SyncItem,
            "guides" => GuidesItem,
            "settings" => SettingsItem,
            _ => AddonsItem,
        });
        ViewModel.ShowRestedXpSignIn = () => _ = ShowRestedXpSignInAsync();
        ViewModel.ShowChannelDialog = channel => ShowDialogAsync(new ReleaseChannelDialog(channel));
        ViewModel.ShowConfirmDialog = ConfirmAsync;
        ViewModel.ShowLinkDialog = ShowMessageAsync;
        ViewModel.ChooseLinkFile = ChooseLinkFileAsync;
        ViewModel.ShowForeverCopyDialog = ShowForeverCopyAsync;
        ViewModel.ChooseGuild = ChooseGuildAsync;
        ViewModel.QuitRequested = QuitCompletely;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        InstallFlyout.OverlayInputPassThroughElement = InstallPicker;
        InstallFlyout.Opening += (_, _) => InstallPickerRunningDot.Opacity = 0;
        InstallFlyout.Closed += (_, _) => InstallPickerRunningDot.Opacity = 1;
        AccountFlyout.Opening += (_, _) => PlaceAccountFlyout();
        AccountName.SizeChanged += (_, _) => PlaceRolePill();
        RolePill.SizeChanged += (_, _) => PlaceRolePill();
        AttachSubmenu();
        AppTitleBar.LayoutUpdated += (_, _) => PositionInstallPicker();
        SignInUrlBox.AddHandler(PointerReleasedEvent, (_, _) => SignInUrlBox.SelectAll(), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        Activated += (_, _) =>
        {
            if (ForegroundGate.Release())
            {
                _logger.Info("Window activated after a start without focus");
            }

            UpdateCaptionButtons();
            _ = ViewModel.EvaluateCurseForgeDefaultHandlerAsync();
        };
        // A press in the window is the user's own choice of it, even when no Activated has been raised for a start without focus (a Store relaunch, 17 Oct 2026).
        AddHandler(PointerPressedEvent, (_, _) =>
        {
            if (ForegroundGate.Release())
            {
                _logger.Info("Window pressed before activation after a start without focus");
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        Deactivated += (_, _) => UpdateCaptionButtons();
        Opened += (_, _) => UpdateCaptionButtons();
        Closing += OnWindowClosing;
        PropertyChanged += OnWindowPropertyChanged;
        if (OperatingSystem.IsWindows())
        {
            Win32Properties.AddWndProcHookCallback(this, OnWndProc);
        }

        Navigate(AddonsItem);
    }

    public MainViewModel ViewModel { get; }

    public ForegroundGate ForegroundGate { get; } = new();

    public Control? CurrentPage => PageHost.Content as Control;

    private Flyout InstallFlyout => (Flyout)InstallPicker.Flyout!;

    private Flyout AccountFlyout => (Flyout)AccountButton.Flyout!;

    private Flyout GuildFlyout => (Flyout)GuildRow.Flyout!;

    private TranslateTransform InstallPickerOffset => (TranslateTransform)InstallPicker.RenderTransform!;

    partial void ConfigurePlatformWindow();

    public void CreateTrayIcon()
    {
        TrayMenuFocus.Attach();
        using var icon = AssetLoader.Open(new Uri("avares://Steward.Desktop/Assets/Steward.ico"));
        var refresh = new NativeMenuItem("Check for updates");
        refresh.Click += (_, _) => ViewModel.RefreshCommand.Execute(null);
        var open = new NativeMenuItem("Open Steward");
        open.Click += (_, _) => ShowFromTray();
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => QuitCompletely();
        var trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(icon),
            ToolTipText = AppInfo.WindowTitle,
            Menu = [open, refresh, new NativeMenuItemSeparator(), quit],
        };
        trayIcon.Clicked += (_, _) => ShowFromTray();
        TrayIcon.SetIcons(Application.Current!, [trayIcon]);
    }

    private void Navigate(Button item)
    {
        if (_selectedNav == item)
        {
            return;
        }

        _selectedNav?.Classes.Remove("Selected");
        item.Classes.Add("Selected");
        _selectedNav = item;
        ViewModel.IsSettingsShown = item == SettingsItem;
        ShowPage(item.Tag switch
        {
            "sync" => typeof(SyncPage),
            "guides" => typeof(GuidesPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(HomePage),
        });
    }

    private void OnNavItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button item)
        {
            Navigate(item);
        }
    }

    private void ShowPage(Type pageType)
    {
        if (PageHost.Content?.GetType() == pageType)
        {
            return;
        }

        var page = (Control)_services.GetRequiredService(pageType);
        PageHost.Content = page;
        if (_pageShown && _shown)
        {
            PlayEntrance(page);
        }

        _pageShown = true;
    }

    private static void PlayEntrance(Control page)
    {
        page.RenderTransform = new TranslateTransform(0, 18);
        var ease = new CubicEaseOut();
        var duration = TimeSpan.FromMilliseconds(220);
        _ = new Animation
        {
            Duration = duration,
            Easing = ease,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, 18d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) } },
            },
        }.RunAsync(page);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Toast))
        {
            ShowToast(ViewModel.Toast);
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.SyncVisibility) && !ViewModel.SyncVisibility && PageHost.Content is SyncPage)
        {
            Navigate(AddonsItem);
            return;
        }

        if (e.PropertyName is not nameof(MainViewModel.GuidesVisibility)
            || ViewModel.GuidesVisibility
            || PageHost.Content is not GuidesPage
            || (ViewModel.RestedXp.IsSessionExpired && ViewModel.HasGuidesFeature))
        {
            return;
        }

        Navigate(AddonsItem);
    }

    private void ShowToast(string? message)
    {
        if (message is not null)
        {
            ToastText.Text = message;
        }

        ToastHost.Opacity = message is null ? 0 : 1;
        ToastHost.RenderTransform = TransformOperations.Parse(message is null ? "translateY(16px)" : "translateY(0px)");
    }

    // FluentAvalonia binds the caption buttons' inactive class to an ancestor Window, but the decorations layer is the window's sibling under its host.
    private void UpdateCaptionButtons()
    {
        foreach (var button in this.GetVisualParent()?.GetVisualDescendants().OfType<Button>() ?? [])
        {
            if (button.Classes.Contains("CaptionButton"))
            {
                button.Classes.Set("inactive", !IsActive);
            }
        }
    }

    private void PlaceRolePill()
    {
        var beside = AccountName.Bounds.Width + RolePillGap + RolePill.Bounds.Width <= AccountNameColumn.Bounds.Width;
        Grid.SetRow(RolePill, beside ? 0 : 1);
        RolePill.HorizontalAlignment = beside ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        RolePill.VerticalAlignment = beside ? VerticalAlignment.Top : VerticalAlignment.Center;
        RolePill.Margin = beside ? default : new Thickness(0, 8, 0, 0);
    }

    private void PositionInstallPicker()
    {
        var width = InstallPicker.Bounds.Width;
        if (_quitting || width <= 0 || InstallPickerHost.Bounds.Width <= 0)
        {
            return;
        }

        var hostLeft = InstallPickerHost.Bounds.X;
        var room = InstallPickerHost.Bounds.Width - width;
        var centred = (AppTitleBar.Bounds.Width - width) / 2 - hostLeft;
        var x = room >= 2 * InstallPickerGap ? Math.Clamp(centred, InstallPickerGap, room - InstallPickerGap) : Math.Max(0, room);
        if (InstallPickerOffset.X != x)
        {
            InstallPickerOffset.X = x;
        }
    }

    private void AttachSubmenu()
    {
        _submenuOpen.Interval = _submenuClose.Interval = Native.SubmenuDelay();
        _submenuOpen.Tick += (_, _) =>
        {
            _submenuOpen.Stop();
            if (!GuildFlyout.IsOpen)
            {
                GuildFlyout.ShowAt(GuildRow);
            }
        };
        _submenuClose.Tick += (_, _) =>
        {
            _submenuClose.Stop();
            GuildFlyout.Hide();
        };

        void ScheduleClose()
        {
            _submenuOpen.Stop();
            if (GuildFlyout.IsOpen)
            {
                _submenuClose.Start();
            }
        }

        GuildRow.PointerEntered += (_, _) =>
        {
            _submenuClose.Stop();
            if (!GuildFlyout.IsOpen)
            {
                _submenuOpen.Start();
            }
        };
        GuildRow.PointerExited += (_, _) => ScheduleClose();
        GuildFlyoutContent.PointerEntered += (_, _) => _submenuClose.Stop();
        foreach (var other in new Control[] { AccountHeader, OpenGuildPanelButton, AccountSignOutButton })
        {
            other.PointerEntered += (_, _) => ScheduleClose();
        }

        GuildRow.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Right)
            {
                return;
            }

            e.Handled = true;
            _submenuOpen.Stop();
            _submenuClose.Stop();
            GuildFlyout.ShowAt(GuildRow);
        };
        GuildFlyoutContent.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Left or Key.Escape))
            {
                return;
            }

            e.Handled = true;
            GuildFlyout.Hide();
            GuildRow.Focus(NavigationMethod.Directional);
        };
        GuildFlyout.Closed += (_, _) =>
        {
            _submenuOpen.Stop();
            _submenuClose.Stop();
        };
    }

    private void PlaceAccountFlyout()
    {
        var top = AccountButton.TranslatePoint(default, this)?.Y ?? 0;
        AccountFlyout.VerticalOffset = Math.Max(0, Bounds.Height - AccountFlyoutBottomGap - top - AccountButton.Bounds.Height);
    }

    private void OnGuildRowClick(object? sender, RoutedEventArgs e) => _submenuOpen.Stop();

    private void SelectAllSignInUrl(object? sender, FocusChangedEventArgs e) => SignInUrlBox.SelectAll();

    private WowInstallViewModel? TaggedInstall(object? sender, string handler)
    {
        if ((sender as Control)?.Tag is WowInstallViewModel install)
        {
            return install;
        }

        ViewModel.WarnUi($"{handler} could not resolve its {nameof(WowInstallViewModel)} from the element's Tag");
        return null;
    }

    private void OnInstallOptionClick(object? sender, RoutedEventArgs e)
    {
        var install = TaggedInstall(sender, nameof(OnInstallOptionClick));
        InstallFlyout.Hide();
        if (install is not null)
        {
            ViewModel.SelectInstall(install);
        }
    }

    private void OnEditInstallOptionClick(object? sender, RoutedEventArgs e)
    {
        var install = TaggedInstall(sender, nameof(OnEditInstallOptionClick));
        InstallFlyout.Hide();
        if (install is not null)
        {
            ViewModel.RequestEditInstall(install);
        }
    }

    private void OnOpenAddOnsFolderClick(object? sender, RoutedEventArgs e)
    {
        InstallFlyout.Hide();
        ViewModel.SelectedInstall?.OpenFolderCommand.Execute(null);
    }

    private void OnAddInstallClick(object? sender, RoutedEventArgs e)
    {
        InstallFlyout.Hide();
        ViewModel.BrowseCommand.Execute(null);
    }

    private void OnManageInstallsClick(object? sender, RoutedEventArgs e)
    {
        InstallFlyout.Hide();
        ViewModel.OpenSettingsCommand.Execute(null);
        (CurrentPage as SettingsPage)?.ShowInstalls();
    }

    private void OnGuildOptionClick(object? sender, RoutedEventArgs e)
    {
        var tag = (sender as Control)?.Tag;
        GuildFlyout.Hide();
        AccountFlyout.Hide();
        if (tag is GuildOptionViewModel option)
        {
            ViewModel.ChooseGuildOption(option);
        }
        else
        {
            ViewModel.WarnUi($"{nameof(OnGuildOptionClick)} could not resolve its {nameof(GuildOptionViewModel)} from the element's Tag");
        }
    }

    private void OnGuildPanelClick(object? sender, RoutedEventArgs e)
    {
        AccountFlyout.Hide();
        ShellOpen.Open("https://guild.hoobi.io");
    }

    private void OnSignOutClick(object? sender, RoutedEventArgs e)
    {
        AccountFlyout.Hide();
        Navigate(AddonsItem);
        ViewModel.SignOutCommand.Execute(null);
    }

    public void ShowGuidesPreview(string scenario)
    {
        Width = 1100;
        Height = 720;
        if (GuidesPreview.OpensDialog(scenario))
        {
            DispatcherTimer.RunOnce(() => _ = ShowRestedXpSignInAsync(), TimeSpan.FromSeconds(1));
            return;
        }

        Navigate(GuidesItem);
    }

    public void ShowPopupsPreview(string scenario)
    {
        Width = 1100;
        Height = 720;
        if (scenario == "settings")
        {
            Navigate(SettingsItem);
            return;
        }

        var uninstall = new MenuItem { Header = "Uninstall" };
        uninstall.Classes.Add("Critical");
        var menu = new MenuFlyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Items = { new MenuItem { Header = "Ignore updates" }, new MenuItem { Header = "Change channel" }, new MenuItem { Header = "Hide addon" }, new MenuItem { Header = "Open folder" }, new MenuItem { Header = "Reinstall" }, new Separator(), uninstall },
        };
        FlyoutBase flyout = scenario == "menu" ? menu : AccountFlyout;
        Control inside = scenario == "menu" ? uninstall : AccountHeader;
        flyout.Closed += (_, _) => _logger.Info($"Popups preview {scenario}: closed");
        DispatcherTimer.RunOnce(
            () =>
            {
                flyout.ShowAt(scenario == "menu" ? TitleText : AccountButton);
                DispatcherTimer.RunOnce(() => LogPopupPreview(scenario, inside), TimeSpan.FromSeconds(1));
            },
            TimeSpan.FromSeconds(2.5));
    }

    private void LogPopupPreview(string scenario, Control inside)
    {
        TemplatedControl? presenter = scenario == "menu" ? inside.FindAncestorOfType<MenuFlyoutPresenter>() : inside.FindAncestorOfType<FlyoutPresenter>();
        if (presenter is null || TopLevel.GetTopLevel(presenter) is not { } top)
        {
            _logger.Info($"Popups preview {scenario}: no presenter");
            return;
        }

        var origin = presenter.PointToScreen(default);
        _logger.Info($"Popups preview {scenario}: top {top.GetType().Name}, transparency {top.ActualTransparencyLevel}, acrylic {Steward.Desktop.Styles.Acrylic.GetMode(presenter)}, owner active {IsActive}, presenter at {origin} size {presenter.Bounds.Size} corner {presenter.CornerRadius}, scaling {top.RenderScaling}, window at {Position}");
    }

    public async Task ShowRestedXpSignInAsync()
    {
        await AppDialogs.ShowAsync(new RestedXpSignInDialog(ViewModel.RestedXp), this);

        if (ViewModel.RestedXp.IsSignedIn)
        {
            Navigate(GuidesItem);
        }
        else if (PageHost.Content is GuidesPage)
        {
            Navigate(AddonsItem);
        }
    }

    private async Task ShowDialogAsync(FAContentDialog dialog) => await AppDialogs.ShowAsync(dialog, this);

    private Task<bool> ConfirmAsync(string title, string body, string primary) => ShowMessageAsync(title, body, primary, "Cancel");

    private static TextBlock Wrapped(string text, bool secondary = false, double? fontSize = null)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (secondary)
        {
            block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable("TextFillColorSecondaryBrush"));
        }

        if (fontSize is { } size)
        {
            block.FontSize = size;
        }

        return block;
    }

    private async Task<bool> ShowMessageAsync(string title, string body, string? primary, string close)
    {
        var dialog = new FAContentDialog
        {
            Title = Wrapped(title),
            Content = Wrapped(body),
            PrimaryButtonText = primary ?? "",
            CloseButtonText = close,
            DefaultButton = primary is null ? FAContentDialogButton.Close : FAContentDialogButton.None,
        };
        return await AppDialogs.ShowAsync(dialog, this) == FAContentDialogResult.Primary;
    }

    private async Task<int?> ChooseLinkFileAsync(string title, IReadOnlyList<string> options, int selected, string hint)
    {
        var files = new ComboBox { ItemsSource = options, SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
        var dialog = new FAContentDialog
        {
            Title = Wrapped(title),
            Content = new StackPanel { Spacing = 8, Children = { files, Wrapped(hint, secondary: true, fontSize: 12) } },
            PrimaryButtonText = "Install",
            CloseButtonText = "Skip",
            DefaultButton = FAContentDialogButton.Primary,
        };
        return await AppDialogs.ShowAsync(dialog, this) == FAContentDialogResult.Primary && files.SelectedIndex >= 0 ? files.SelectedIndex : null;
    }

    private async Task<bool?> ShowForeverCopyAsync(bool firstRun, ForeverCopyPlan plan, Func<bool, bool, bool, bool, Task> copy)
    {
        static string Count(int count, string one, string many, string rest) => $"{count} {(count == 1 ? one : many)} {rest}";

        static CheckBox Option(string title, string? detail, string empty = "Nothing newer on the beta", bool? isChecked = null)
        {
            var option = new CheckBox
            {
                IsChecked = isChecked ?? detail is not null,
                IsEnabled = detail is not null,
                Padding = new Thickness(8, 0, 0, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children = { new TextBlock { Text = title }, Wrapped(detail ?? empty, secondary: true, fontSize: 12) },
                },
            };
            // The CheckBox template top-aligns its glyph in a 32px box, which leaves it high beside a two-line label.
            option.TemplateApplied += (_, e) =>
            {
                if (e.NameScope.Find<Border>("NormalRectangle")?.GetVisualParent() is Layoutable glyph)
                {
                    glyph.VerticalAlignment = VerticalAlignment.Center;
                }
            };
            return option;
        }

        var addonCounts = new[]
        {
            plan.NewFolders.Count > 0 ? Count(plan.NewFolders.Count, "addon", "addons", "to copy") : null,
            plan.UpdatedFolders.Count > 0 ? plan.NewFolders.Count > 0 ? $"{plan.UpdatedFolders.Count} to update" : Count(plan.UpdatedFolders.Count, "addon", "addons", "to update") : null,
        }.OfType<string>().ToList();
        var addons = Option("Addons", addonCounts.Count > 0 ? string.Join(", ", addonCounts) : null);
        var savedVariables = Option(
            "Account saved variables",
            plan.SavedVariablesFiles.Count > 0 ? Count(plan.SavedVariablesFiles.Count, "file", "files", "newer on the beta") : null);
        var settings = Option(
            "Account settings (keybindings, macros and interface options)",
            plan.SettingsFiles.Count > 0 ? Count(plan.SettingsFiles.Count, "file", "files", "newer on the beta") : null);
        var clientSettings = Option(
            "Graphics, sound and narration",
            plan.ClientSettings.Count > 0 ? Count(plan.ClientSettings.Count, "setting differs", "settings differ", "from the beta") : null,
            plan.LiveConfigMissing ? "Start Forever once first" : "Same as the beta",
            plan.ClientSettings.Count > 0 && plan.ClientSettingsNewer);
        var progress = new ProgressBar { IsIndeterminate = true, IsVisible = false };
        var dialog = new FAContentDialog
        {
            Title = Wrapped(firstRun ? "Copy from the beta" : "Sync from the beta"),
            Content = new StackPanel
            {
                Spacing = 8,
                Children = { Wrapped("Steward copies what is newer on the beta. Nothing on Forever is deleted."), addons, savedVariables, settings, clientSettings, progress },
            },
            PrimaryButtonText = firstRun ? "Copy" : "Sync",
            SecondaryButtonText = firstRun ? "I'll do it myself" : "",
            CloseButtonText = firstRun ? "Not now" : "Cancel",
            DefaultButton = FAContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            addons.IsEnabled = savedVariables.IsEnabled = settings.IsEnabled = clientSettings.IsEnabled = dialog.IsPrimaryButtonEnabled = dialog.IsSecondaryButtonEnabled = false;
            progress.IsVisible = true;
            try
            {
                await copy(addons.IsChecked == true, savedVariables.IsChecked == true, settings.IsChecked == true, clientSettings.IsChecked == true);
            }
            finally
            {
                deferral.Complete();
            }
        };
        return await AppDialogs.ShowAsync(dialog, this) switch
        {
            FAContentDialogResult.Primary => true,
            FAContentDialogResult.Secondary => false,
            _ => null,
        };
    }

    private async Task<GuildOptionViewModel?> ChooseGuildAsync(IReadOnlyList<GuildOptionViewModel> guilds, GuildOptionViewModel preselected)
    {
        if (!IsVisible)
        {
            return null;
        }

        var dialog = new ChooseGuildDialog(guilds, preselected);
        await AppDialogs.ShowAsync(dialog, this);
        return dialog.Selected;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_quitting)
        {
            return;
        }

        e.Cancel = true;
        if (OperatingSystem.IsMacOS())
        {
            HideToTray();
            return;
        }

        if (ViewModel.CloseToTray && TrayHost.IsAvailable)
        {
            MinimizeThenHideToTray();
            return;
        }

        _ = ConfirmCloseAsync();
    }

    private async Task ConfirmCloseAsync()
    {
        if (_confirmingClose)
        {
            return;
        }

        _confirmingClose = true;
        try
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            var dialog = new FAContentDialog
            {
                Title = Wrapped("Are you sure you want to close?"),
                Content = Wrapped("This will prevent automatic addon updates and guild syncing.", secondary: true),
                PrimaryButtonText = "Close anyway",
                SecondaryButtonText = TrayHost.IsAvailable ? "Minimize to tray" : "Minimize",
            };
            dialog.Opened += (_, _) =>
            {
                var buttons = dialog.GetVisualDescendants().OfType<Button>().ToList();
                if (buttons.FirstOrDefault(b => b.Name == "PrimaryButton") is { } primary && this.TryFindResource("CriticalButtonStyle", out var critical) && critical is ControlTheme theme)
                {
                    primary.Theme = theme;
                }

                buttons.FirstOrDefault(b => b.Name == "SecondaryButton")?.Focus();
            };

            switch (await AppDialogs.ShowAsync(dialog, this))
            {
                case FAContentDialogResult.Primary:
                    QuitCompletely();
                    break;
                case FAContentDialogResult.Secondary:
                    MinimizeThenHideToTray();
                    break;
            }
        }
        finally
        {
            _confirmingClose = false;
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty
            && WindowState == WindowState.Minimized
            && (ViewModel.MinimizeToTray || _hideWhenMinimized))
        {
            _hideWhenMinimized = false;
            HideAfterMinimizeAnimation();
        }
    }

    private void HideAfterMinimizeAnimation()
    {
        if (!Native.MinimizeAnimationEnabled())
        {
            HideToTray();
            return;
        }

        // The OS raises no event when the minimise animation ends, and hiding earlier cancels it.
        _minimizeHideTimer?.Stop();
        _minimizeHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _minimizeHideTimer.Tick += (_, _) =>
        {
            _minimizeHideTimer.Stop();
            if (WindowState == WindowState.Minimized)
            {
                HideToTray();
            }
        };
        _minimizeHideTimer.Start();
    }

    private void MinimizeThenHideToTray()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            _hideWhenMinimized = true;
            WindowState = WindowState.Minimized;
            return;
        }

        HideToTray();
    }

    public void HideToTray()
    {
        if (!TrayHost.IsAvailable)
        {
            ShowWithoutFocus();
            WindowState = WindowState.Minimized;
            return;
        }

        Hide();
        Native.SetEfficiencyMode(true);
        _trimTimer?.Stop();
        _trimTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _trimTimer.Tick += (_, _) =>
        {
            _trimTimer.Stop();
            TrimIfHidden();
        };
        _trimTimer.Start();
    }

    public void TrimIfHidden()
    {
        if (!IsVisible)
        {
            Native.TrimWorkingSet();
        }
    }

    public void ShowFromTray()
    {
        ForegroundGate.Release();
        _minimizeHideTimer?.Stop();
        _trimTimer?.Stop();
        Native.SetEfficiencyMode(false);
        ShowActivated = true;
        ShowRendered();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Native.ForceForeground(TryGetPlatformHandle()?.Handle ?? 0);
        ViewModel.ResumeGuildPrompt();
    }

    public void ShowWithoutFocus()
    {
        _trimTimer?.Stop();
        Native.SetEfficiencyMode(false);
        ShowActivated = false;
        ShowRendered();
    }

    private void ShowRendered()
    {
        if (_shown)
        {
            if (!IsVisible)
            {
                Show();
            }

            return;
        }

        _shown = true;
        var handle = TryGetPlatformHandle()?.Handle ?? 0;
        Native.Cloak(handle, cloaked: true);
        Show();
        RequestAnimationFrame(_ => RequestAnimationFrame(_ => Native.Cloak(handle, cloaked: false)));
    }

    private void QuitCompletely()
    {
        _quitting = true;
        foreach (var trayIcon in TrayIcon.GetIcons(Application.Current!) ?? [])
        {
            trayIcon.Dispose();
        }
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    // Caps the wait at 10s so a busy row can never blow the newcomer's own 15s budget for the global mutex.
    public async Task QuitFromAnotherBuildAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (ViewModel.IsAnyRowBusy && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        QuitCompletely();
    }

    // Windows sends these to request or confirm a logoff, reboot or app-update shutdown; unhandled, the app would not exit until Windows force-kills it.
    private nint OnWndProc(nint hWnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        // Moving onto a drawn caption button ends client tracking with WM_MOUSELEAVE right after Avalonia's non-client move set the hover, and Windows resends the move only 0.6s to 1.7s later.
        if (msg == Native.WM_MOUSELEAVE)
        {
            Native.RepostCaptionButtonMove(hWnd);
        }

        if (msg is Native.WM_QUERYENDSESSION or Native.WM_ENDSESSION)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_quitting)
                {
                    _logger.Info("Session ending; quitting");
                    QuitCompletely();
                }
            });
        }

        return 0;
    }
}
