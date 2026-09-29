using CommunityToolkit.Mvvm.Input;

using Steward.Core;
using Steward.Core.Diagnostics;

using Microsoft.UI.Xaml;

namespace Steward.App.ViewModels;

public sealed partial class MainViewModel
{
    private const string MissingBannerPrefix = "missing-install|";

    private HashSet<string> _missingInstalls = new(StringComparer.OrdinalIgnoreCase);

    private IEnumerable<WowInstallViewModel> PresentInstalls => Installs.Where(install => !install.IsMissing);

    public Visibility MissingInstallVisibility => When(SelectedInstall is { IsMissing: true });

    public string MissingInstallBody => SelectedInstall is { IsMissing: true } install
        ? $"Steward cannot find {install.FlavourPath}. Remove the install, or restore the folder and refresh."
        : "";

    private void DetectMissingInstalls()
    {
        var report = MissingInstalls.Detect(
            _stateStore.Load(),
            DateTimeOffset.Now,
            Directory.Exists,
            path => Directory.Exists(Path.GetPathRoot(path)));
        _stateStore.Save(report.State);

        foreach (var path in report.DueForRemoval)
        {
            _logger.Info($"Removing install {path}: its folder has been missing since {report.State.MissingSince[path]:u}");
            RemoveInstall(path);
        }

        _missingInstalls = new HashSet<string>(report.Missing.Except(report.DueForRemoval, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    }

    private void ApplyMissingInstalls()
    {
        DetectMissingInstalls();

        var state = _stateStore.Load();
        foreach (var install in Installs.ToList())
        {
            var missing = _missingInstalls.Contains(install.FlavourPath);
            if (install.IsMissing == missing)
            {
                continue;
            }

            var rebuilt = missing
                ? WowInstalls.MissingFromPath(install.FlavourPath, _supportedProducts, state.InstallProducts)
                : WowInstalls.FromFlavourPath(install.FlavourPath, _supportedProducts, state.InstallProducts);
            if (rebuilt is not null)
            {
                ReplaceInstall(install, rebuilt);
            }
        }

        EnsureSelection();
        ShowMissingInstallBanners();
        RecomputeSummary();
    }

    private void ReplaceInstall(WowInstallViewModel install, WowInstall rebuilt)
    {
        var index = Installs.IndexOf(install);
        var wasSelected = SelectedInstall == install;
        DetachInstall(install);
        var replacement = AddInstall(rebuilt, isAddedByUser: true);
        Installs.Move(Installs.IndexOf(replacement), Math.Clamp(index, 0, Installs.Count - 1));
        replacement.ApplyStatus(_status, background: false);
        if (wasSelected)
        {
            SelectedInstall = replacement;
        }
    }

    private void ShowMissingInstallBanners()
    {
        _localBanners.RemoveAll(banner =>
            banner.Id.StartsWith(MissingBannerPrefix, StringComparison.Ordinal)
            && !_missingInstalls.Contains(banner.Id[MissingBannerPrefix.Length..]));

        var state = _stateStore.Load();
        foreach (var install in Installs.Where(install => install.IsMissing))
        {
            var path = install.FlavourPath;
            var id = MissingBannerPrefix + path;
            var episode = $"{id}|{state.MissingSince.GetValueOrDefault(path).UtcTicks}";
            if ((state.DismissedBanners ?? []).ContainsKey(episode) || _localBanners.Any(banner => banner.Id == id))
            {
                continue;
            }

            ShowLocalInfoBanner(
                $"{install.Label}'s folder no longer exists.",
                id,
                "Remove",
                () => _ = ConfirmRemoveInstallAsync(path),
                () => _stateStore.Save(_stateStore.Load() with
                {
                    DismissedBanners = new Dictionary<string, int>(_stateStore.Load().DismissedBanners ?? [], StringComparer.Ordinal) { [episode] = 0 },
                }));
        }

        RefreshBanners();
    }

    private async Task ConfirmRemoveInstallAsync(string flavourPath)
    {
        if (Installs.FirstOrDefault(install => string.Equals(install.FlavourPath, flavourPath, StringComparison.OrdinalIgnoreCase)) is not { } install
            || ShowConfirmDialog is not { } show)
        {
            return;
        }

        if (await show($"Remove {install.Label}?", $"Steward stops managing {flavourPath}. Nothing in that folder is deleted.", "Remove").ConfigureAwait(true))
        {
            RemoveInstall(flavourPath);
        }
    }

    [RelayCommand]
    private Task RemoveSelectedInstallAsync() =>
        SelectedInstall is { } install ? ConfirmRemoveInstallAsync(install.FlavourPath) : Task.CompletedTask;
}
