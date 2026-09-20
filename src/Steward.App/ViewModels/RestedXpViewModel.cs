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
    Written,
    Failed,
    NeedsNewerAddon,
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

    public GuideRowViewModel(RestedXpInstallViewModel card, string productName, DateTimeOffset? updatedAt, bool isFirst, bool isAllowed)
    {
        _card = card;
        _updatedAt = updatedAt;
        ProductName = productName;
        IsFirst = isFirst;
        IsAllowed = isAllowed;
    }

    public string ProductName { get; }

    public bool IsFirst { get; }

    public bool IsAllowed { get; }

    public Thickness HairlineThickness => IsFirst ? default : new Thickness(0, 1, 0, 0);

    public string? RowTooltip => IsAllowed ? null : $"{ProductName} is for another client. This install is {_card.DisplayName}.";

    public string UpdatedText => !IsAllowed
        ? "Not for this client"
        : _updatedAt is { } at
            ? $"Updated {RelativeTime.Describe(at, DateTimeOffset.Now)}"
            : "Not published yet";

    public Brush UpdatedBrush => (Brush)Application.Current.Resources[
        IsAllowed ? "TextFillColorSecondaryBrush" : "TextFillColorTertiaryBrush"];

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial GuideRowState State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MessageText))]
    public partial string? FailureText { get; set; }

    public Visibility PillVisibility => When(IsSelected && State != GuideRowState.None);

    public string PillText => State switch
    {
        GuideRowState.InGame => "Up to date",
        GuideRowState.Downloading => "Downloading",
        GuideRowState.Written => "Written · imports on next login or /reload",
        GuideRowState.Failed => "Failed",
        GuideRowState.NeedsNewerAddon => "Needs a newer guide",
        _ => string.Empty,
    };

    public Brush PillBrush => (Brush)Application.Current.Resources[State switch
    {
        GuideRowState.InGame or GuideRowState.Written => "SystemFillColorSuccessBrush",
        GuideRowState.Downloading => "AccentTextFillColorPrimaryBrush",
        GuideRowState.NeedsNewerAddon => "SystemFillColorCautionBrush",
        GuideRowState.Failed => "SystemFillColorCriticalBrush",
        _ => "TextFillColorSecondaryBrush",
    }];

    public Brush RowBackground => !IsSelected ? NoTint : State switch
    {
        GuideRowState.Downloading => (Brush)Application.Current.Resources["InfoTintBrush"],
        GuideRowState.NeedsNewerAddon => (Brush)Application.Current.Resources["CautionTintBrush"],
        GuideRowState.Failed => (Brush)Application.Current.Resources["CriticalTintBrush"],
        _ => NoTint,
    };

    public Visibility ProgressVisibility =>
        When(IsSelected && State is GuideRowState.Downloading or GuideRowState.NeedsNewerAddon);

    public Visibility MessageVisibility =>
        When(IsSelected && State is GuideRowState.Failed or GuideRowState.NeedsNewerAddon);

    public string MessageText => State switch
    {
        GuideRowState.Failed => FailureText ?? "Download failed. Retrying in 10 s.",
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
        if (!value)
        {
            State = GuideRowState.None;
        }

        _card.SelectionChanged();
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

    public IReadOnlyList<GuideRowViewModel> SelectedRows => [.. Rows.Where(row => row.IsSelected)];

    public IReadOnlyList<string> SelectedProducts => [.. SelectedRows.Select(row => row.ProductName)];

    [ObservableProperty]
    public partial string? AddonVersion { get; set; }

    [ObservableProperty]
    public partial bool IsSessionActive { get; set; } = true;

    public Visibility RowsVisibility => When(Rows.Count > 0);

    public Visibility NoGuidesVisibility => When(Rows.Count == 0);

    public void SetProducts(
        IReadOnlyList<string> products,
        IReadOnlyCollection<string> selected,
        IReadOnlyDictionary<string, long> timestamps,
        Func<string, bool> isAllowed)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(timestamps);
        ArgumentNullException.ThrowIfNull(isAllowed);

        _isLoading = true;
        Rows.Clear();
        foreach (var product in products)
        {
            var updatedAt = timestamps.TryGetValue(product, out var timestamp)
                ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                : (DateTimeOffset?)null;
            var allowed = isAllowed(product);
            Rows.Add(new GuideRowViewModel(this, product, updatedAt, Rows.Count == 0, allowed)
            {
                IsSelected = allowed && selected.Contains(product, StringComparer.Ordinal),
            });
        }

        _isLoading = false;
        OnPropertyChanged(nameof(RowsVisibility));
        OnPropertyChanged(nameof(NoGuidesVisibility));
    }

    public void SelectionChanged()
    {
        if (!_isLoading)
        {
            _ = _choiceChanged(this);
        }
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
                LoadProducts(card);
                Guides.Add(card);
            }

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
            LoadProducts(card);
            await SyncAsync(card).ConfigureAwait(true);
        }

        BattleTag = _service.BattleTag;
    }

    private void LoadProducts(RestedXpInstallViewModel card) => card.SetProducts(
        _service.Products,
        [.. _service.GuideChoices(card.Install)],
        _service.Timestamps,
        product => _service.IsAllowed(card.Install, product));

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
                return;
            }

            IsMfaRequired = await _service.SignInAsync(UsernameInput, PasswordInput, CancellationToken.None).ConfigureAwait(true);
            if (!IsMfaRequired)
            {
                await AfterSignInAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            PasswordInput = string.Empty;
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
        PasswordInput = string.Empty;
        ErrorMessage = null;
        if (!IsPreview)
        {
            _service.ForgetPendingPassword();
        }
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
            card.SetProducts([], [], _service.Timestamps, _ => false);
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
        _service.SetGuideChoices(card.FlavourPath, card.SelectedProducts);
        await SyncAsync(card).ConfigureAwait(true);
    }

    private async Task SyncAsync(RestedXpInstallViewModel card)
    {
        if (IsPreview)
        {
            return;
        }

        var rows = card.SelectedRows;
        foreach (var row in rows)
        {
            row.State = GuideRowState.Downloading;
        }

        try
        {
            var results = await _service.SyncAsync(card.Install, card.SelectedProducts, CancellationToken.None).ConfigureAwait(true);
            foreach (var row in rows)
            {
                row.FailureText = null;
                row.State = results.TryGetValue(row.ProductName, out var result) ? Map(result) : GuideRowState.None;
            }

            Confirm(card);
        }
        catch (RestedXpSessionExpiredException)
        {
            DropSession();
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            ErrorMessage = ex.Message;
            foreach (var row in rows)
            {
                row.State = GuideRowState.Failed;
            }
        }
    }

    public void Confirm(string flavourPath)
    {
        var card = Guides.FirstOrDefault(g => string.Equals(g.FlavourPath, flavourPath, StringComparison.OrdinalIgnoreCase));
        if (card is not null)
        {
            Confirm(card);
        }
    }

    private void Confirm(RestedXpInstallViewModel card)
    {
        if (IsPreview)
        {
            return;
        }

        var results = _service.Confirm(card.Install, card.SelectedProducts);
        foreach (var row in card.SelectedRows.Where(row => row.State is GuideRowState.InGame or GuideRowState.Written))
        {
            if (results.TryGetValue(row.ProductName, out var result))
            {
                row.FailureText = result.Outcome is GuideSyncOutcome.Rejected ? result.Error : null;
                row.State = Map(result);
            }
        }
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is HttpRequestException or JsonException or NotSupportedException or OperationCanceledException or RestedXpSignInException;

    private static GuideRowState Map(GuideSyncResult result) => result switch
    {
        { Error: not null } => GuideRowState.Failed,
        { Outcome: GuideSyncOutcome.UpToDate } => GuideRowState.InGame,
        { Outcome: GuideSyncOutcome.Written } => GuideRowState.Written,
        { Outcome: GuideSyncOutcome.NeedsNewerAddon } => GuideRowState.NeedsNewerAddon,
        _ => GuideRowState.Downloading,
    };
}
