using System.Collections.ObjectModel;
using System.Text.Json;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.App.Services;
using Steward.Core;

using Microsoft.UI.Dispatching;
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
    Unfinished,
    ChangedOnDisk,
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

    public GuideRowViewModel(RestedXpInstallViewModel card, string productName, Uri? imageUri, DateTimeOffset? updatedAt, bool isAllowed)
    {
        _card = card;
        _updatedAt = updatedAt;
        ProductName = productName;
        Image = imageUri is null ? null : ManifestIcon.For($"restedxp-{productName}", imageUri);
        IsAllowed = isAllowed;
    }

    public string ProductName { get; }

    public ImageSource? Image { get; }

    public double ImageOpacity => IsAllowed ? 1 : 0.4;

    public bool IsAllowed { get; }

    public string? RowTooltip => IsAllowed ? null : $"{ProductName} is for another client. This install is {_card.DisplayName}.";

    public string UpdatedText => !IsAllowed
        ? "Not for this client"
        : _updatedAt is { } at
            ? $"Updated {RelativeTime.Describe(at, DateTimeOffset.Now)}"
            : "Not published yet";

    public Brush UpdatedBrush => (Brush)Application.Current.Resources[
        IsAllowed ? "TextFillColorSecondaryBrush" : "TextFillColorTertiaryBrush"];

    public Brush NameBrush => (Brush)Application.Current.Resources[
        IsAllowed ? "TextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush"];

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
        GuideRowState.Unfinished => "Not finished",
        GuideRowState.ChangedOnDisk => "Changed on disk",
        _ => string.Empty,
    };

    public Brush PillBrush => (Brush)Application.Current.Resources[State switch
    {
        GuideRowState.InGame or GuideRowState.Written => "SystemFillColorSuccessBrush",
        GuideRowState.Downloading => "AccentTextFillColorPrimaryBrush",
        GuideRowState.NeedsNewerAddon or GuideRowState.Unfinished or GuideRowState.ChangedOnDisk => "SystemFillColorCautionBrush",
        GuideRowState.Failed => "SystemFillColorCriticalBrush",
        _ => "TextFillColorSecondaryBrush",
    }];

    public Brush RowBackground => !IsSelected ? NoTint : State switch
    {
        GuideRowState.Downloading => (Brush)Application.Current.Resources["InfoTintBrush"],
        GuideRowState.NeedsNewerAddon or GuideRowState.Unfinished or GuideRowState.ChangedOnDisk => (Brush)Application.Current.Resources["CautionTintBrush"],
        GuideRowState.Failed => (Brush)Application.Current.Resources["CriticalTintBrush"],
        _ => NoTint,
    };

    public Visibility ProgressVisibility =>
        When(IsSelected && State is GuideRowState.Downloading or GuideRowState.NeedsNewerAddon);

    public Visibility MessageVisibility =>
        When(IsSelected && State is GuideRowState.Failed or GuideRowState.NeedsNewerAddon or GuideRowState.Unfinished or GuideRowState.ChangedOnDisk);

    public string MessageText => State switch
    {
        GuideRowState.Failed => FailureText ?? "Download failed. Retrying in 10 s.",
        GuideRowState.NeedsNewerAddon => $"Fetching a build for RXPGuides {_card.AddonVersion}",
        GuideRowState.Unfinished => "Import did not finish; it runs again on the next login",
        GuideRowState.ChangedOnDisk => "Edited outside Steward. Use Write again to restore it.",
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
        DisplayName = install.DisplayName;
        _choiceChanged = choiceChanged;
    }

    public WowInstall Install { get; }

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    public string FlavourPath => Install.FlavourPath;

    public ObservableCollection<GuideRowViewModel> Rows { get; } = [];

    public IReadOnlyList<GuideRowViewModel> SelectedRows => [.. Rows.Where(row => row.IsSelected)];

    public IReadOnlyList<string> SelectedProducts => [.. SelectedRows.Select(row => row.ProductName)];

    [ObservableProperty]
    public partial string? AddonVersion { get; set; }

    [ObservableProperty]
    public partial ImageSource? AddonIcon { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BaseGuidesText))]
    public partial DateTimeOffset? AddonReleasedAt { get; set; }

    public string BaseGuidesText => AddonReleasedAt is { } at
        ? $"Included with the addon, updated {RelativeTime.Describe(at, DateTimeOffset.Now)}"
        : "Included with the addon";

    [ObservableProperty]
    public partial bool IsSessionActive { get; set; } = true;

    public Visibility NoGuidesVisibility => When(Rows.Count == 0);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WrittenText))]
    public partial DateTimeOffset? WrittenAt { get; set; }

    public string GuidesFilePath => Path.Combine(Install.AddOnsPath, StewardGuidesAddon.FolderName, "Guides.lua");

    public string WrittenText => WrittenAt is { } at ? $" · written {SyncViewModel.Relative(at)}" : " · not written yet";

    public Visibility ReloadNoticeVisibility(WowClientProcess? client, DateTimeOffset? writtenAt) =>
        When(SavedVariablesFreshness.AwaitsReload(Install.FlavourPath, writtenAt, client));

    public void RefreshRelativeTimes()
    {
        OnPropertyChanged(nameof(WrittenText));
        OnPropertyChanged(nameof(WrittenAt));
        OnPropertyChanged(nameof(BaseGuidesText));
    }

    public void SetProducts(
        IReadOnlyList<string> products,
        IReadOnlyCollection<string> selected,
        IReadOnlyDictionary<string, long> timestamps,
        IReadOnlyDictionary<string, Uri> images,
        Func<string, bool> isAllowed)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(timestamps);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(isAllowed);

        _isLoading = true;
        Rows.Clear();
        foreach (var product in products)
        {
            var updatedAt = timestamps.TryGetValue(product, out var timestamp)
                ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                : (DateTimeOffset?)null;
            var allowed = isAllowed(product);
            Rows.Add(new GuideRowViewModel(this, product, images.GetValueOrDefault(product), updatedAt, allowed)
            {
                IsSelected = allowed && selected.Contains(product, StringComparer.Ordinal),
            });
        }

        _isLoading = false;
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

public sealed partial class RestedXpViewModel : ObservableObject, IDisposable
{
    public const string AddonId = "restedxp";

    private static readonly TimeSpan PreviewDelay = TimeSpan.FromSeconds(2);

    private readonly RestedXpService _service;
    private readonly Func<bool> _hasGuidesFeature;
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Dictionary<string, SavedVariablesWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);

    public RestedXpViewModel(RestedXpService service, Func<bool> hasGuidesFeature)
    {
        _service = service;
        _hasGuidesFeature = hasGuidesFeature;
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
    [NotifyPropertyChangedFor(nameof(SubtitleDetails))]
    public partial string? BattleTag { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitleDetails))]
    public partial DateTimeOffset? LastCheckedAt { get; set; }

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

    public string SubtitleDetails => string.Concat(
        string.IsNullOrEmpty(BattleTag) ? "" : $" · {BattleTag}",
        LastCheckedAt is { } at ? $" · checked {SyncViewModel.Relative(at)}" : "");

    public void RefreshRelativeTimes()
    {
        OnPropertyChanged(nameof(SubtitleDetails));
        foreach (var card in Guides)
        {
            card.RefreshRelativeTimes();
        }
    }

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

            card.DisplayName = install.Label;
            card.IsSessionActive = IsSignedIn;
            var addon = install.AddonRows.FirstOrDefault(row => string.Equals(row.AddonId, AddonId, StringComparison.OrdinalIgnoreCase));
            card.AddonVersion = addon?.InstalledVersion;
            card.AddonIcon = addon?.Icon;
            card.AddonReleasedAt = addon?.ReleasedAt;
        }

        SyncWatchers();
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    private void SyncWatchers()
    {
        if (!_hasGuidesFeature())
        {
            foreach (var watcher in _watchers.Values)
            {
                watcher.Dispose();
            }

            _watchers.Clear();
            return;
        }

        foreach (var gone in _watchers.Keys.Where(path => !Guides.Any(g => string.Equals(g.FlavourPath, path, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            _watchers[gone].Dispose();
            _watchers.Remove(gone);
        }

        foreach (var card in Guides.Where(g => !_watchers.ContainsKey(g.FlavourPath)))
        {
            var flavourPath = card.FlavourPath;
            var watcher = SavedVariablesWatcher.TryCreate(
                flavourPath,
                StewardGuidesSavedVariables.FileName,
                () => _dispatcher?.TryEnqueue(() => Confirm(flavourPath)));
            if (watcher is not null)
            {
                _watchers[flavourPath] = watcher;
            }
        }
    }

    public async Task RefreshSessionAsync()
    {
        if (IsPreview || !_hasGuidesFeature())
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
        if (IsPreview || !_service.IsSignedIn || !_hasGuidesFeature())
        {
            return;
        }

        try
        {
            await _service.EnsureFreshSessionAsync(forCall: true, CancellationToken.None).ConfigureAwait(true);
            await _service.LoadCatalogueAsync(CancellationToken.None).ConfigureAwait(true);
            LastCheckedAt = DateTimeOffset.Now;
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

    private void LoadProducts(RestedXpInstallViewModel card)
    {
        card.SetProducts(
            _service.Products,
            [.. _service.GuideChoices(card.Install)],
            _service.Timestamps,
            _service.ProductImages,
            product => _service.IsAllowed(card.Install, product));
        card.WrittenAt = _service.WrittenAt(card.Install);
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
        LastCheckedAt = null;
        IsMfaRequired = false;
        IsSessionExpired = false;
        ErrorMessage = null;
        foreach (var card in Guides)
        {
            card.SetProducts([], [], _service.Timestamps, _service.ProductImages, _ => false);
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

    public async Task RewriteAfterInstallAsync(WowInstall install)
    {
        var card = Guides.FirstOrDefault(card => string.Equals(card.FlavourPath, install.FlavourPath, StringComparison.OrdinalIgnoreCase));
        if (card is not null && IsSignedIn && card.SelectedProducts.Count > 0)
        {
            await SyncAsync(card).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task WriteAgainAsync()
    {
        foreach (var card in Guides.Where(card => card.SelectedProducts.Count > 0))
        {
            await SyncAsync(card, force: true).ConfigureAwait(true);
        }
    }

    private async Task SyncAsync(RestedXpInstallViewModel card, bool force = false)
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
            var results = await _service.SyncAsync(card.Install, card.SelectedProducts, CancellationToken.None, force).ConfigureAwait(true);
            foreach (var row in rows)
            {
                row.FailureText = null;
                row.State = results.TryGetValue(row.ProductName, out var result) ? Map(result) : GuideRowState.None;
            }

            card.WrittenAt = _service.WrittenAt(card.Install);
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
        if (IsPreview || !_hasGuidesFeature())
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
        { Outcome: GuideSyncOutcome.Unfinished } => GuideRowState.Unfinished,
        { Outcome: GuideSyncOutcome.ChangedOnDisk } => GuideRowState.ChangedOnDisk,
        { Error: not null } => GuideRowState.Failed,
        { Outcome: GuideSyncOutcome.NotInstalled } => GuideRowState.None,
        { Outcome: GuideSyncOutcome.UpToDate } => GuideRowState.InGame,
        { Outcome: GuideSyncOutcome.Written } => GuideRowState.Written,
        { Outcome: GuideSyncOutcome.NeedsNewerAddon } => GuideRowState.NeedsNewerAddon,
        _ => GuideRowState.Downloading,
    };
}
