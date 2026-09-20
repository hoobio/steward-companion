using System.Collections.ObjectModel;
using System.Text.Json;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.App.Services;
using Steward.Core;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Steward.App.ViewModels;

public enum GuideRowState
{
    None,
    InGame,
    Downloading,
    Waiting,
    Written,
    Failed,
    NeedsNewerAddon,
    NoAccountFiles,
}

public sealed partial class GuideRowViewModel : ObservableObject
{
    private static readonly string[] DerivedNames =
    [
        nameof(PillVisibility),
        nameof(PillText),
        nameof(PillBrush),
        nameof(RowBackground),
        nameof(ProgressVisibility),
        nameof(MessageVisibility),
        nameof(MessageText),
        nameof(MessageBrush),
        nameof(RetryVisibility),
        nameof(UpdatedVisibility),
    ];

    private static readonly SolidColorBrush NoTint = new(Microsoft.UI.Colors.Transparent);

    private readonly RestedXpInstallViewModel _card;
    private readonly DateTimeOffset? _updatedAt;

    public GuideRowViewModel(RestedXpInstallViewModel card, string productName, DateTimeOffset? updatedAt, bool isFirst)
    {
        _card = card;
        _updatedAt = updatedAt;
        ProductName = productName;
        IsFirst = isFirst;
    }

    public string ProductName { get; }

    public bool IsFirst { get; }

    public string GroupName => _card.FlavourPath;

    public Thickness HairlineThickness => IsFirst ? default : new Thickness(0, 1, 0, 0);

    public string UpdatedText => _updatedAt is { } at
        ? $"Updated {RelativeTime.Describe(at, DateTimeOffset.Now)}"
        : "Not published yet";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial GuideRowState State { get; set; }

    public Visibility PillVisibility => When(IsSelected && State != GuideRowState.None);

    public string PillText => State switch
    {
        GuideRowState.InGame => "In game",
        GuideRowState.Downloading => "Downloading",
        GuideRowState.Waiting => "Waiting for game to close",
        GuideRowState.Written => "Written · imports on next login",
        GuideRowState.Failed => "Failed",
        GuideRowState.NeedsNewerAddon => "Needs a newer guide",
        _ => "Waiting for RXPGuides to run once",
    };

    public Brush PillBrush => (Brush)Application.Current.Resources[State switch
    {
        GuideRowState.InGame or GuideRowState.Written => "SystemFillColorSuccessBrush",
        GuideRowState.Downloading => "AccentTextFillColorPrimaryBrush",
        GuideRowState.Waiting or GuideRowState.NeedsNewerAddon => "SystemFillColorCautionBrush",
        GuideRowState.Failed => "SystemFillColorCriticalBrush",
        _ => "TextFillColorSecondaryBrush",
    }];

    public Brush RowBackground => !IsSelected ? NoTint : State switch
    {
        GuideRowState.Downloading => (Brush)Application.Current.Resources["InfoTintBrush"],
        GuideRowState.Waiting or GuideRowState.NeedsNewerAddon => (Brush)Application.Current.Resources["CautionTintBrush"],
        GuideRowState.Failed => (Brush)Application.Current.Resources["CriticalTintBrush"],
        _ => NoTint,
    };

    public Visibility ProgressVisibility =>
        When(IsSelected && State is GuideRowState.Downloading or GuideRowState.NeedsNewerAddon);

    public Visibility MessageVisibility =>
        When(IsSelected && State is GuideRowState.Failed or GuideRowState.NeedsNewerAddon);

    public string MessageText => State switch
    {
        GuideRowState.Failed => "Download failed. Retrying in 10 s.",
        GuideRowState.NeedsNewerAddon => $"Fetching a build for RXPGuides {_card.AddonVersion}",
        _ => string.Empty,
    };

    public Brush MessageBrush => (Brush)Application.Current.Resources[
        State == GuideRowState.Failed ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush"];

    public Visibility RetryVisibility => When(IsSelected && State == GuideRowState.Failed);

    public Visibility UpdatedVisibility => When(MessageVisibility == Visibility.Collapsed);

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    [RelayCommand]
    private Task RetryAsync() => _card.ResyncAsync();

    partial void OnIsSelectedChanged(bool value)
    {
        NotifyDerived();
        if (value)
        {
            _card.Select(this);
        }
    }

    partial void OnStateChanged(GuideRowState value) => NotifyDerived();

    private void NotifyDerived()
    {
        foreach (var name in DerivedNames)
        {
            OnPropertyChanged(name);
        }
    }
}

public sealed partial class RestedXpInstallViewModel : ObservableObject
{
    private readonly Func<RestedXpInstallViewModel, Task> _choiceChanged;
    private bool _isLoading;

    public RestedXpInstallViewModel(WowInstall install, Func<RestedXpInstallViewModel, Task> choiceChanged)
    {
        Install = install;
        _choiceChanged = choiceChanged;
    }

    public WowInstall Install { get; }

    public string DisplayName => Install.DisplayName;

    public string FlavourPath => Install.FlavourPath;

    public ObservableCollection<GuideRowViewModel> Rows { get; } = [];

    public GuideRowViewModel? SelectedRow => Rows.FirstOrDefault(row => row.IsSelected);

    [ObservableProperty]
    public partial string? SelectedProduct { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunningVisibility))]
    public partial bool IsClientRunning { get; set; }

    [ObservableProperty]
    public partial string? AddonVersion { get; set; }

    [ObservableProperty]
    public partial bool IsSessionActive { get; set; } = true;

    public Visibility RunningVisibility => When(IsClientRunning);

    public Visibility RowsVisibility => When(Rows.Count > 0);

    public Visibility NoGuidesVisibility => When(Rows.Count == 0);

    public void SetProducts(IReadOnlyList<string> products, string? selected, IReadOnlyDictionary<string, long> timestamps)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(timestamps);

        _isLoading = true;
        Rows.Clear();
        foreach (var product in products)
        {
            var updatedAt = timestamps.TryGetValue(product, out var timestamp)
                ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                : (DateTimeOffset?)null;
            Rows.Add(new GuideRowViewModel(this, product, updatedAt, Rows.Count == 0)
            {
                IsSelected = string.Equals(product, selected, StringComparison.Ordinal),
            });
        }

        SelectedProduct = SelectedRow?.ProductName;
        _isLoading = false;
        OnPropertyChanged(nameof(RowsVisibility));
        OnPropertyChanged(nameof(NoGuidesVisibility));
    }

    public void Select(GuideRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (_isLoading)
        {
            return;
        }

        SelectedProduct = row.ProductName;
        foreach (var other in Rows.Where(other => other != row))
        {
            other.State = GuideRowState.None;
        }

        _ = _choiceChanged(this);
    }

    public Task ResyncAsync() => _choiceChanged(this);

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;
}

public sealed partial class RestedXpViewModel : ObservableObject
{
    public const string AddonId = "restedxp";

    private static readonly TimeSpan PreviewDelay = TimeSpan.FromSeconds(2);

    private readonly RestedXpService _service;

    public RestedXpViewModel(RestedXpService service)
    {
        _service = service;
        IsSignedIn = service.TryRestore();
        SignedInAs = service.Username;
        BattleTag = service.BattleTag;
    }

    public ObservableCollection<RestedXpInstallViewModel> Guides { get; } = [];

    [ObservableProperty]
    public partial bool IsSignedIn { get; set; }

    [ObservableProperty]
    public partial string? SignedInAs { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BattleTagVisibility))]
    public partial string? BattleTag { get; set; }

    [ObservableProperty]
    public partial bool IsSessionExpired { get; set; }

    [ObservableProperty]
    public partial string UsernameInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PasswordInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MfaCode { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MfaVisibility), nameof(CredentialsVisibility))]
    public partial bool IsMfaRequired { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorVisibility))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(BusyVisibility))]
    public partial bool IsBusy { get; set; }

    public bool IsPreview { get; private set; }

    public Visibility BattleTagVisibility => When(!string.IsNullOrEmpty(BattleTag));

    public Visibility MfaVisibility => When(IsMfaRequired);

    public Visibility CredentialsVisibility => When(!IsMfaRequired);

    public Visibility ErrorVisibility => When(!string.IsNullOrEmpty(ErrorMessage));

    public Visibility BusyVisibility => When(IsBusy);

    public bool IsIdle => !IsBusy;

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    public void ApplyPreview(string email, string battleTag, RestedXpInstallViewModel card, bool signedIn, bool expired)
    {
        IsPreview = true;
        Guides.Clear();
        if (card is not null)
        {
            Guides.Add(card);
        }

        SignedInAs = email;
        BattleTag = battleTag;
        IsSignedIn = signedIn;
        IsSessionExpired = expired;
    }

    public void SetInstalls(IEnumerable<WowInstallViewModel> installs)
    {
        ArgumentNullException.ThrowIfNull(installs);

        if (IsPreview)
        {
            return;
        }

        var wanted = installs.ToList();
        foreach (var gone in Guides.Where(g => !wanted.Any(i => string.Equals(i.FlavourPath, g.FlavourPath, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            Guides.Remove(gone);
        }

        foreach (var install in wanted)
        {
            var card = Guides.FirstOrDefault(g => string.Equals(g.FlavourPath, install.FlavourPath, StringComparison.OrdinalIgnoreCase));
            if (card is null)
            {
                card = new RestedXpInstallViewModel(install.Install, OnChoiceChangedAsync);
                card.SetProducts(_service.Products, _service.GuideChoice(install.FlavourPath) ?? _service.DefaultProduct, _service.Timestamps);
                Guides.Add(card);
            }

            card.IsClientRunning = install.IsClientRunning;
            card.IsSessionActive = IsSignedIn;
            card.AddonVersion = install.AddonRows
                .FirstOrDefault(row => string.Equals(row.AddonId, AddonId, StringComparison.OrdinalIgnoreCase))?.InstalledVersion;
        }
    }

    public async Task RefreshSessionAsync()
    {
        if (IsPreview)
        {
            return;
        }

        try
        {
            await _service.EnsureFreshSessionAsync(forCall: false, CancellationToken.None).ConfigureAwait(true);
        }
        catch (RestedXpSessionExpiredException)
        {
            DropSession();
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task CheckAsync()
    {
        if (IsPreview || !_service.IsSignedIn)
        {
            return;
        }

        try
        {
            await _service.EnsureFreshSessionAsync(forCall: true, CancellationToken.None).ConfigureAwait(true);
            await _service.LoadCatalogueAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (RestedXpSessionExpiredException)
        {
            DropSession();
            return;
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            ErrorMessage = ex.Message;
            return;
        }

        foreach (var card in Guides.ToList())
        {
            card.SetProducts(_service.Products, _service.GuideChoice(card.FlavourPath) ?? _service.DefaultProduct, _service.Timestamps);
            await SyncAsync(card).ConfigureAwait(true);
        }

        BattleTag = _service.BattleTag;
    }

    public async Task WritePendingAsync(WowInstall install)
    {
        ArgumentNullException.ThrowIfNull(install);

        if (IsPreview)
        {
            return;
        }

        var card = Guides.FirstOrDefault(g => string.Equals(g.FlavourPath, install.FlavourPath, StringComparison.OrdinalIgnoreCase));
        if (card is not null)
        {
            await SyncAsync(card, cacheOnly: true).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (string.IsNullOrWhiteSpace(UsernameInput) || string.IsNullOrEmpty(PasswordInput))
        {
            ErrorMessage = "Enter your RestedXP username and password.";
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            if (IsPreview)
            {
                await Task.Delay(PreviewDelay).ConfigureAwait(true);
                IsMfaRequired = true;
                PasswordInput = string.Empty;
                return;
            }

            IsMfaRequired = await _service.SignInAsync(UsernameInput, PasswordInput, CancellationToken.None).ConfigureAwait(true);
            PasswordInput = string.Empty;
            if (!IsMfaRequired)
            {
                await AfterSignInAsync().ConfigureAwait(true);
            }
        }
        catch (RestedXpSignInException)
        {
            ErrorMessage = "Wrong username or password";
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task VerifyMfaAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            if (IsPreview)
            {
                await Task.Delay(PreviewDelay).ConfigureAwait(true);
                ErrorMessage = "That code was not accepted";
                return;
            }

            await _service.VerifyMfaAsync(UsernameInput, MfaCode, CancellationToken.None).ConfigureAwait(true);
            MfaCode = string.Empty;
            IsMfaRequired = false;
            await AfterSignInAsync().ConfigureAwait(true);
        }
        catch (RestedXpSignInException)
        {
            ErrorMessage = "That code was not accepted";
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Back()
    {
        IsMfaRequired = false;
        MfaCode = string.Empty;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void SignOut()
    {
        _service.SignOut();
        IsSignedIn = false;
        SignedInAs = null;
        BattleTag = null;
        IsMfaRequired = false;
        IsSessionExpired = false;
        ErrorMessage = null;
        foreach (var card in Guides)
        {
            card.SetProducts([], null, _service.Timestamps);
            card.IsSessionActive = false;
        }
    }

    private async Task AfterSignInAsync()
    {
        IsSignedIn = _service.IsSignedIn;
        IsSessionExpired = false;
        SignedInAs = _service.Username;
        UsernameInput = string.Empty;
        foreach (var card in Guides)
        {
            card.IsSessionActive = true;
        }

        await CheckAsync().ConfigureAwait(true);
    }

    private void DropSession()
    {
        IsSignedIn = false;
        IsSessionExpired = true;
        foreach (var card in Guides)
        {
            card.IsSessionActive = false;
        }
    }

    private async Task OnChoiceChangedAsync(RestedXpInstallViewModel card)
    {
        if (card.SelectedProduct is not { } product)
        {
            return;
        }

        _service.SetGuideChoice(card.FlavourPath, product);
        await SyncAsync(card).ConfigureAwait(true);
    }

    private async Task SyncAsync(RestedXpInstallViewModel card, bool cacheOnly = false)
    {
        if (IsPreview)
        {
            return;
        }

        if (card.SelectedRow is not { } row)
        {
            return;
        }

        if (!cacheOnly)
        {
            row.State = GuideRowState.Downloading;
        }

        try
        {
            if (await _service.SyncAsync(card.Install, row.ProductName, cacheOnly, CancellationToken.None).ConfigureAwait(true) is { } result)
            {
                row.State = Map(result);
            }
            else if (!cacheOnly)
            {
                row.State = GuideRowState.None;
            }
        }
        catch (RestedXpSessionExpiredException)
        {
            DropSession();
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            ErrorMessage = ex.Message;
            row.State = GuideRowState.Failed;
        }
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is HttpRequestException or JsonException or NotSupportedException or OperationCanceledException or RestedXpSignInException;

    private static GuideRowState Map(GuideSyncResult result) => result switch
    {
        { Error: not null } => GuideRowState.Failed,
        { Outcome: GuideSyncOutcome.UpToDate } => GuideRowState.InGame,
        { Outcome: GuideSyncOutcome.Stale } => GuideRowState.Downloading,
        { Outcome: GuideSyncOutcome.Downloaded } => GuideRowState.Waiting,
        { Outcome: GuideSyncOutcome.Written } => GuideRowState.Written,
        { Outcome: GuideSyncOutcome.NeedsNewerAddon } => GuideRowState.NeedsNewerAddon,
        _ => GuideRowState.NoAccountFiles,
    };
}
