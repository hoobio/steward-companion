using CommunityToolkit.Mvvm.Input;

using Steward.Core;
using Steward.Core.Diagnostics;

using Microsoft.UI.Xaml;

namespace Steward.App.ViewModels;

public sealed partial class MainViewModel
{
    private const string ForeverCopyBannerId = "forever-copy";

    private WowInstall? _heldLiveInstall;

    public Func<bool, ForeverCopyPlan, Func<bool, bool, bool, Task>, Task<bool?>>? ShowForeverCopyDialog { get; set; }

    private WowInstallViewModel? BetaInstall => PresentInstalls.FirstOrDefault(install => install.Install.ProductCode == ForeverLaunch.BetaProduct);

    public Visibility SyncFromBetaVisibility =>
        When(BetaInstall is not null && SelectedInstall is { IsMissing: false } install && install.Install.ProductCode == ForeverLaunch.LiveProduct);

    [RelayCommand]
    private Task SyncFromBetaAsync() =>
        SelectedInstall is { } live ? OpenForeverCopyAsync(live.Install, firstRun: false) : Task.CompletedTask;

    private void ApplyForeverGate()
    {
        var state = _stateStore.Load();
        if (_heldLiveInstall is { } held && !ForeverCopy.IsGated(held, [.. Installs.Select(install => install.Install), held], state.ForeverCopyDone))
        {
            _heldLiveInstall = null;
            if (!Installs.Any(install => string.Equals(install.FlavourPath, held.FlavourPath, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.Info($"Forever install {held.FlavourPath} released from the copy-from-beta gate");
                AddInstall(held, state.AddedInstalls.Contains(held.FlavourPath, StringComparer.OrdinalIgnoreCase)).ApplyStatus(_status, background: false);
                EnsureSelection();
            }
        }

        var shown = _localBanners.Any(banner => banner.Id == ForeverCopyBannerId);
        if (_heldLiveInstall is { } live && !shown)
        {
            ShowLocalInfoBanner(
                "Copy your addons to World of Warcraft: Forever",
                ForeverCopyBannerId,
                "Copy from beta",
                () => _ = OpenForeverCopyAsync(live, firstRun: true),
                detail: "Steward can copy the beta's addons, and how Steward manages them, to the live client.",
                dismissible: false);
        }
        else if (_heldLiveInstall is null && shown)
        {
            _localBanners.RemoveAll(banner => banner.Id == ForeverCopyBannerId);
            RefreshBanners();
        }

        OnPropertyChanged(nameof(SyncFromBetaVisibility));
    }

    private string? ForeverCopyRefusal(WowInstallViewModel beta, WowInstall live)
    {
        if (WowClient.IsRunning(beta.Install) || WowClient.IsRunning(live))
        {
            return "Close World of Warcraft first. Steward cannot copy from the beta while the game is running.";
        }

        return beta.IsAnyRowBusy || Installs.Any(install => string.Equals(install.FlavourPath, live.FlavourPath, StringComparison.OrdinalIgnoreCase) && install.IsAnyRowBusy)
            ? "Wait for the addon updates to finish, then try again."
            : null;
    }

    private async Task OpenForeverCopyAsync(WowInstall live, bool firstRun)
    {
        if (BetaInstall is not { } beta || ShowForeverCopyDialog is not { } show)
        {
            return;
        }

        if (ForeverCopyRefusal(beta, live) is { } refusal)
        {
            StatusMessage = refusal;
            return;
        }

        var managed = ManagedFolders(live.FlavourPath);
        ForeverCopyPlan plan;
        try
        {
            plan = await Task.Run(() => ForeverCopy.Plan(beta.Install, live, managed)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warn(ex, $"Could not compare {beta.FlavourPath} with {live.FlavourPath}");
            StatusMessage = $"Could not read the beta's files: {ex.Message}";
            return;
        }

        _logger.Info($"Forever copy offered: {plan.NewFolders.Count} folder(s) to copy, {plan.UpdatedFolders.Count} to update, {plan.SavedVariablesFiles.Count} saved variable file(s) and {plan.SettingsFiles.Count} settings file(s) newer on the beta");
        var choice = await show(firstRun, plan, (addons, savedVariables, settings) => RunForeverCopyAsync(beta, live, managed, addons, savedVariables, settings)).ConfigureAwait(true);
        if (choice is false)
        {
            _logger.Info($"Forever copy declined for {live.FlavourPath}; the user copies by hand");
            MarkForeverCopyDone(live.FlavourPath);
        }
    }

    private async Task RunForeverCopyAsync(WowInstallViewModel beta, WowInstall live, IReadOnlyList<string> managed, bool addons, bool savedVariables, bool settings)
    {
        if (ForeverCopyRefusal(beta, live) is { } refusal)
        {
            StatusMessage = refusal;
            return;
        }

        _logger.Info($"Forever copy from {beta.FlavourPath} to {live.FlavourPath}: addons {addons}, saved variables {savedVariables}, game settings {settings}");
        ForeverCopyResult result;
        try
        {
            result = await Task.Run(() => ForeverCopy.Sync(beta.Install, live, managed, addons, savedVariables, settings,
                (path, ex) => _logger.Warn(ex, $"Forever copy could not write {path}"))).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warn(ex, $"Forever copy to {live.FlavourPath} failed");
            StatusMessage = $"Could not copy from the beta: {ex.Message}";
            return;
        }

        _logger.Info($"Forever copy done: {result.Copied} copied, {result.Updated} updated, {result.SavedVariablesFiles} saved variable file(s), {result.SettingsFiles} settings file(s), {result.Failures} failure(s)");
        var catalogue = _addons.Concat(_addonCatalogue ?? []).ToList();
        _stateStore.Save(ForeverCopy.CarryState(
            _stateStore.Load(),
            beta.FlavourPath,
            live.FlavourPath,
            CurseForgeVersionType(live),
            folder => Directory.Exists(Path.Combine(live.AddOnsPath, folder)),
            id => catalogue.FirstOrDefault(addon => string.Equals(addon.Id, id, StringComparison.OrdinalIgnoreCase))?.FolderName));
        MarkForeverCopyDone(live.FlavourPath);

        if (Installs.FirstOrDefault(install => string.Equals(install.FlavourPath, live.FlavourPath, StringComparison.OrdinalIgnoreCase)) is { } liveInstall)
        {
            ReplaceInstall(liveInstall, liveInstall.Install, liveInstall.IsAddedByUser);
            RecomputeSummary();
            _ = ProbeAddedProviderAddonsAsync(ProviderAddons(live.FlavourPath));
        }

        var parts = new[]
        {
            result.Copied > 0 ? $"{result.Copied} {(result.Copied == 1 ? "addon" : "addons")} copied" : null,
            result.Updated > 0 ? $"{result.Updated} updated" : null,
            result.SavedVariablesFiles > 0 ? $"{result.SavedVariablesFiles} saved variable {(result.SavedVariablesFiles == 1 ? "file" : "files")}" : null,
            result.SettingsFiles > 0 ? $"{result.SettingsFiles} game settings {(result.SettingsFiles == 1 ? "file" : "files")}" : null,
        }.OfType<string>().ToList();
        ShowLocalInfoBanner(parts.Count > 0 ? $"Synced from the beta: {string.Join(", ", parts)}." : "Synced from the beta: nothing on the beta was newer.");
        if (result.Failures > 0)
        {
            StatusMessage = $"{result.Failures} {(result.Failures == 1 ? "file" : "files")} could not be copied from the beta. The log names each one.";
        }
    }

    private void MarkForeverCopyDone(string flavourPath)
    {
        var state = _stateStore.Load();
        if (!state.ForeverCopyDone.Contains(flavourPath, StringComparer.OrdinalIgnoreCase))
        {
            state.ForeverCopyDone.Add(flavourPath);
            _stateStore.Save(state);
        }

        ApplyForeverGate();
    }

    private IReadOnlyList<string> ManagedFolders(string flavourPath)
    {
        var prefix = AppStateStore.Key(flavourPath, string.Empty);
        return
        [
            .. _addons.Concat(_addonCatalogue ?? []).Select(addon => addon.FolderName),
            StewardGuidesAddon.FolderName,
            .. ProviderRecords(flavourPath).SelectMany(CurseForgeAddons.Folders),
            .. _stateStore.Load().Installs
                .Where(entry => entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .SelectMany(entry => entry.Value.Folders ?? []),
        ];
    }
}
