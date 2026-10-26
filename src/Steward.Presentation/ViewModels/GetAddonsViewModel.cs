using System.Collections.ObjectModel;
using System.Text.Json;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.App.Services;
using Steward.Core;
using Steward.Core.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Steward.App.ViewModels;

public sealed partial class GetAddonsViewModel : ObservableObject
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    private readonly MainViewModel _main;
    private readonly WowInstallViewModel _install;
    private readonly int _versionType;
    private readonly StewardClient _client;
    private readonly TukUiSource _tukUi;
    private readonly ILogger _logger;
    private IReadOnlyList<CurseForgeResult>? _discover;
    private CancellationTokenSource? _searchCts;

    public GetAddonsViewModel(MainViewModel main, WowInstallViewModel install, int versionType, StewardClient client, TukUiSource tukUi, ILogger logger)
    {
        _main = main;
        _install = install;
        _versionType = versionType;
        _client = client;
        _tukUi = tukUi;
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

    public bool SearchUnavailableVisibility => IsSearchUnavailable;

    public bool LoadingVisibility => IsLoading;

    public bool ErrorVisibility => Error is not null;

    public bool NoResultsVisibility => HasNoResults;

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
            var (tukUi, tukUiError) = await TukUiAsync(query, cancellationToken).ConfigureAwait(true);
            var (results, searched, curseForgeError) = await CurseForgeAsync(query, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            var count = tukUi.Count + results.Count;
            Heading = searched ? $"{count} result{(count == 1 ? "" : "s")} for “{query}”" : $"Popular for {GameVersion}";
            Results.Clear();
            foreach (var addon in tukUi)
            {
                Results.Add(new CurseForgeResultViewModel(TukUiResult(addon), MainViewModel.IsInstalled(_install, TukUiAddons.Id(addon.Id)), _ => _main.InstallTukUiAsync(_install, addon), _main.Platform, TukUiAddons.Source));
            }

            foreach (var result in results)
            {
                Results.Add(new CurseForgeResultViewModel(result, MainViewModel.IsCurseForgeInstalled(_install, result.Id, _versionType), InstallAsync, _main.Platform));
            }

            HasNoResults = Results.Count == 0;
            Error = string.Join(" ", new[] { tukUiError, curseForgeError }.OfType<string>()) is { Length: > 0 } error ? error : null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<(IReadOnlyList<TukUiAddon> Addons, string? Error)> TukUiAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            var listed = await _tukUi.ListAsync(TukUiSource.ListTtl, cancellationToken).ConfigureAwait(true);
            return ([.. listed.Where(addon => addon.Name is { Length: > 0 } name
                && TukUiSource.RunsOn(addon, _install.ClientVersion)
                && (query.Length == 0 || name.Contains(query, StringComparison.OrdinalIgnoreCase) || addon.Slug?.Contains(query, StringComparison.OrdinalIgnoreCase) == true))], null);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.Warn(ex, "TukUI addon list failed");
            return ([], $"Could not reach TukUI: {ex.Message}");
        }
    }

    private async Task<(IReadOnlyList<CurseForgeResult> Results, bool Searched, string? Error)> CurseForgeAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            if (query.Length > 0 && !IsSearchUnavailable)
            {
                var found = await _client.SearchCurseForgeAsync(_versionType, query, cancellationToken).ConfigureAwait(true);
                IsSearchUnavailable = found is null;
                if (found is not null)
                {
                    return (found, true, null);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return (_discover ??= await _client.GetCurseForgeDiscoverAsync(_versionType, cancellationToken).ConfigureAwait(true), false, null);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is HttpRequestException or StewardRequestException or SessionExpiredException or TaskCanceledException or JsonException)
        {
            _logger.Warn(ex, "CurseForge discover or search failed");
            return ([], query.Length > 0, ex is SessionExpiredException ? "Your session has expired. Sign in again to get addons." : $"Could not reach CurseForge: {ex.Message}");
        }
    }

    private static CurseForgeResult TukUiResult(TukUiAddon addon) =>
        new(addon.Id, addon.Name!, addon.SmallDesc, addon.Author, TukUiSource.Https(addon.LogoSquareUrl), TukUiSource.Https(addon.WebUrl), TukUiSource.DisplayVersion(addon));

    private Task<string?> InstallAsync(CurseForgeResult result) => _main.InstallCurseForgeAsync(_install, result, _versionType);
}

public sealed partial class CurseForgeResultViewModel : ObservableObject
{
    private readonly Func<CurseForgeResult, Task<string?>> _install;

    public CurseForgeResultViewModel(CurseForgeResult result, bool isInstalled, Func<CurseForgeResult, Task<string?>> install, IUiPlatform platform, string source = CurseForgeAddons.Source)
    {
        ArgumentNullException.ThrowIfNull(platform);

        Result = result;
        Source = source;
        IsInstalled = isInstalled;
        _install = install;
        Icon = Uri.TryCreate(result.IconUrl, UriKind.Absolute, out var icon) ? platform.ImageFromUri(icon, 30) : null;
        InitialsColor = InitialsTile.Color(result.Name);
        WebsiteUri = Uri.TryCreate(result.WebsiteUrl, UriKind.Absolute, out var website) && website.Scheme == Uri.UriSchemeHttps ? website : null;
    }

    public CurseForgeResult Result { get; }

    public string Source { get; }

    public string SourcePrefix => $"{Source} · ";

    public string Name => Result.Name;

    public string ByAuthor => Result.Author is { Length: > 0 } author ? $" by {author}" : "";

    public string Summary => Result.Summary ?? "";

    public string LatestVersion => Result.LatestVersion ?? "";

    public string Initials => InitialsTile.Text(Name);

    public uint InitialsColor { get; }

    public object? Icon { get; }

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

    public bool InstallVisibility => Result.AllowDistribution && !IsInstalled && !IsInstalling;

    public bool InstallingVisibility => IsInstalling;

    public bool InstalledVisibility => IsInstalled;

    public bool LinkVisibility => !Result.AllowDistribution && !IsInstalled && WebsiteUri is not null;

    public bool FailureVisibility => Failure is not null;

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
