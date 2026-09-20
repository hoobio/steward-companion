using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.App.Services;
using Steward.Core;

using Microsoft.UI.Xaml;

namespace Steward.App.ViewModels;

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

    public ObservableCollection<string> Products { get; } = [];

    [ObservableProperty]
    public partial string? SelectedProduct { get; set; }

    [ObservableProperty]
    public partial string StatusLine { get; set; } = "Not checked yet";

    public void SetProducts(IReadOnlyList<string> products, string? selected)
    {
        _isLoading = true;
        Products.Clear();
        foreach (var product in products)
        {
            Products.Add(product);
        }

        SelectedProduct = selected is not null && products.Contains(selected) ? selected : null;
        _isLoading = false;
    }

    partial void OnSelectedProductChanged(string? value)
    {
        if (_isLoading || value is null)
        {
            return;
        }

        _ = _choiceChanged(this);
    }
}

public sealed partial class RestedXpViewModel : ObservableObject
{
    private readonly RestedXpService _service;

    public RestedXpViewModel(RestedXpService service)
    {
        _service = service;
        IsSignedIn = service.TryRestore();
        SignedInAs = service.Username;
    }

    public ObservableCollection<RestedXpInstallViewModel> Guides { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SignedInVisibility), nameof(SignedOutVisibility))]
    public partial bool IsSignedIn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SignedInAsText))]
    public partial string? SignedInAs { get; set; }

    [ObservableProperty]
    public partial string UsernameInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PasswordInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MfaCode { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MfaVisibility))]
    public partial bool IsMfaRequired { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorVisibility))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public string SignedInAsText => $"Signed in as {SignedInAs}";

    public Visibility SignedInVisibility => When(IsSignedIn);

    public Visibility SignedOutVisibility => When(!IsSignedIn);

    public Visibility MfaVisibility => When(IsMfaRequired);

    public Visibility ErrorVisibility => When(!string.IsNullOrEmpty(ErrorMessage));

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    public void SetInstalls(IEnumerable<WowInstall> installs)
    {
        ArgumentNullException.ThrowIfNull(installs);

        var wanted = installs.ToList();
        foreach (var gone in Guides.Where(g => !wanted.Any(i => string.Equals(i.FlavourPath, g.FlavourPath, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            Guides.Remove(gone);
        }

        foreach (var install in wanted)
        {
            if (Guides.Any(g => string.Equals(g.FlavourPath, install.FlavourPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var row = new RestedXpInstallViewModel(install, OnChoiceChangedAsync);
            row.SetProducts(_service.Products, _service.GuideChoice(install.FlavourPath) ?? _service.DefaultProduct);
            Guides.Add(row);
        }
    }

    public async Task RefreshSessionAsync()
    {
        try
        {
            await _service.EnsureFreshSessionAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (RestedXpSessionExpiredException ex)
        {
            DropSession(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task CheckAsync()
    {
        if (!_service.IsSignedIn)
        {
            return;
        }

        try
        {
            await _service.EnsureFreshSessionAsync(CancellationToken.None).ConfigureAwait(true);
            await _service.LoadCatalogueAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (RestedXpSessionExpiredException ex)
        {
            DropSession(ex.Message);
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = ex.Message;
            return;
        }

        foreach (var row in Guides.ToList())
        {
            row.SetProducts(_service.Products, _service.GuideChoice(row.FlavourPath) ?? _service.DefaultProduct);
            await SyncAsync(row).ConfigureAwait(true);
        }
    }

    public async Task WritePendingAsync(WowInstall install)
    {
        ArgumentNullException.ThrowIfNull(install);

        var row = Guides.FirstOrDefault(g => string.Equals(g.FlavourPath, install.FlavourPath, StringComparison.OrdinalIgnoreCase));
        if (row is not null)
        {
            await SyncAsync(row).ConfigureAwait(true);
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
            IsMfaRequired = await _service.SignInAsync(UsernameInput, PasswordInput, CancellationToken.None).ConfigureAwait(true);
            PasswordInput = string.Empty;
            if (!IsMfaRequired)
            {
                await AfterSignInAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is RestedXpSignInException or HttpRequestException or TaskCanceledException)
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
            await _service.VerifyMfaAsync(UsernameInput, MfaCode, CancellationToken.None).ConfigureAwait(true);
            MfaCode = string.Empty;
            IsMfaRequired = false;
            await AfterSignInAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is RestedXpSignInException or HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SignOut()
    {
        _service.SignOut();
        IsSignedIn = false;
        SignedInAs = null;
        IsMfaRequired = false;
        ErrorMessage = null;
        foreach (var row in Guides)
        {
            row.SetProducts([], null);
            row.StatusLine = "Not checked yet";
        }
    }

    private async Task AfterSignInAsync()
    {
        IsSignedIn = _service.IsSignedIn;
        SignedInAs = _service.Username;
        await CheckAsync().ConfigureAwait(true);
    }

    private void DropSession(string message)
    {
        IsSignedIn = false;
        SignedInAs = null;
        ErrorMessage = message;
    }

    private async Task OnChoiceChangedAsync(RestedXpInstallViewModel row)
    {
        if (row.SelectedProduct is not { } product)
        {
            return;
        }

        _service.SetGuideChoice(row.FlavourPath, product);
        await SyncAsync(row).ConfigureAwait(true);
    }

    private async Task SyncAsync(RestedXpInstallViewModel row)
    {
        if (row.SelectedProduct is not { } product)
        {
            row.StatusLine = "No RestedXP guide owned";
            return;
        }

        try
        {
            var result = await _service.SyncAsync(row.Install, product, CancellationToken.None).ConfigureAwait(true);
            row.StatusLine = Describe(result);
        }
        catch (RestedXpSessionExpiredException ex)
        {
            DropSession(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            row.StatusLine = ex.Message;
        }
    }

    private static string Describe(GuideSyncResult result) => result switch
    {
        { Error: { } error } => error,
        { Outcome: GuideSyncOutcome.UpToDate } => $"Up to date · updated {RelativeTime.Describe(result.UpdatedAt, DateTimeOffset.Now)}",
        { Outcome: GuideSyncOutcome.Stale } => "Update available · downloading…",
        { Outcome: GuideSyncOutcome.Downloaded } => "Downloaded · written when the game closes",
        { Outcome: GuideSyncOutcome.Written } => "Written · imports on next login",
        _ => "Waiting for RXPGuides to run once on this install",
    };
}
