using System.Collections.ObjectModel;
using System.Text.Json;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.Core;
using Steward.Core.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Steward.App.ViewModels;

public sealed partial class GetAddonsViewModel : ObservableObject
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    private readonly MainViewModel _main;
    private readonly WowInstallViewModel _install;
    private readonly int _versionType;
    private readonly StewardClient _client;
    private readonly ILogger _logger;
    private IReadOnlyList<CurseForgeResult>? _discover;
    private CancellationTokenSource? _searchCts;

    public GetAddonsViewModel(MainViewModel main, WowInstallViewModel install, int versionType, StewardClient client, ILogger logger)
    {
        _main = main;
        _install = install;
        _versionType = versionType;
        _client = client;
        _logger = logger;
        GameVersion = install.GameVersionName ?? install.Label;
    }

    public string GameVersion { get; }

    public string Lede => $"Searching addons built for {GameVersion}.";

    public ObservableCollection<CurseForgeResultViewModel> Results { get; } = [];

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    [ObservableProperty]
    public partial string Heading { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchUnavailableVisibility))]
    public partial bool IsSearchUnavailable { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingVisibility))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorVisibility))]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoResultsVisibility))]
    public partial bool HasNoResults { get; private set; }

    public string NoResultsText => $"No addons for {GameVersion} match. Check the spelling or try another source.";

    public Visibility SearchUnavailableVisibility => When(IsSearchUnavailable);

    public Visibility LoadingVisibility => When(IsLoading);

    public Visibility ErrorVisibility => When(Error is not null);

    public Visibility NoResultsVisibility => When(HasNoResults);

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    public Task LoadAsync() => ShowAsync("", CancellationToken.None);

    partial void OnQueryChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        _ = DebouncedSearchAsync(value.Trim(), _searchCts.Token);
    }

    private async Task DebouncedSearchAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchDebounce, cancellationToken).ConfigureAwait(true);
            await ShowAsync(query, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ShowAsync(string query, CancellationToken cancellationToken)
    {
        IsLoading = true;
        Error = null;
        try
        {
            IReadOnlyList<CurseForgeResult>? results = null;
            if (query.Length > 0 && !IsSearchUnavailable)
            {
                results = await _client.SearchCurseForgeAsync(_versionType, query, cancellationToken).ConfigureAwait(true);
                IsSearchUnavailable = results is null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var searched = results is not null;
            results ??= _discover ??= await _client.GetCurseForgeDiscoverAsync(_versionType, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            Heading = searched ? $"{results.Count} result{(results.Count == 1 ? "" : "s")} for “{query}”" : $"Popular for {GameVersion}";
            Results.Clear();
            foreach (var result in results)
            {
                Results.Add(new CurseForgeResultViewModel(result, MainViewModel.IsCurseForgeInstalled(_install, result.Id, _versionType), InstallAsync));
            }

            HasNoResults = Results.Count == 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or StewardRequestException or SessionExpiredException or TaskCanceledException or JsonException)
        {
            _logger.Warn(ex, "CurseForge discover or search failed");
            Error = ex is SessionExpiredException ? "Your session has expired. Sign in again to get addons." : $"Could not reach CurseForge: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private Task<string?> InstallAsync(CurseForgeResult result) => _main.InstallCurseForgeAsync(_install, result, _versionType);
}

public sealed partial class CurseForgeResultViewModel : ObservableObject
{
    private readonly Func<CurseForgeResult, Task<string?>> _install;

    public CurseForgeResultViewModel(CurseForgeResult result, bool isInstalled, Func<CurseForgeResult, Task<string?>> install)
    {
        Result = result;
        IsInstalled = isInstalled;
        _install = install;
        Icon = Uri.TryCreate(result.IconUrl, UriKind.Absolute, out var icon) ? new BitmapImage(icon) { DecodePixelWidth = 60 } : null;
        InitialsBrush = InitialsTile.Brush(result.Name);
        WebsiteUri = Uri.TryCreate(result.WebsiteUrl, UriKind.Absolute, out var website) && website.Scheme == Uri.UriSchemeHttps ? website : null;
    }

    public CurseForgeResult Result { get; }

    public string Name => Result.Name;

    public string ByAuthor => Result.Author is { Length: > 0 } author ? $" by {author}" : "";

    public string Summary => Result.Summary ?? "";

    public string LatestVersion => Result.LatestVersion ?? "";

    public string Initials => InitialsTile.Text(Name);

    public Brush InitialsBrush { get; }

    public ImageSource? Icon { get; }

    public Uri? WebsiteUri { get; }

    public string InstallAccessibleName => $"Install {Name}";

    public string LinkAccessibleName => $"{Name} is available on CurseForge";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallVisibility), nameof(InstalledVisibility), nameof(LinkVisibility))]
    public partial bool IsInstalled { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallVisibility), nameof(InstallingVisibility))]
    public partial bool IsInstalling { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FailureVisibility))]
    public partial string? Failure { get; private set; }

    public Visibility InstallVisibility => When(Result.AllowDistribution && !IsInstalled && !IsInstalling);

    public Visibility InstallingVisibility => When(IsInstalling);

    public Visibility InstalledVisibility => When(IsInstalled);

    public Visibility LinkVisibility => When(!Result.AllowDistribution && !IsInstalled && WebsiteUri is not null);

    public Visibility FailureVisibility => When(Failure is not null);

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    [RelayCommand]
    private async Task InstallAsync()
    {
        IsInstalling = true;
        Failure = null;
        try
        {
            Failure = await _install(Result).ConfigureAwait(true);
            IsInstalled = Failure is null;
        }
        finally
        {
            IsInstalling = false;
        }
    }
}
