using Steward.Core;
using Steward.Core.Diagnostics;

namespace Steward.App.ViewModels;

public sealed partial class MainViewModel
{
    private const string RetiredBannerId = "retired-addons";
    private const string RetiredDeferredBannerId = "retired-addons-deferred";
    private const string RetiredFailedBannerId = "retired-addons-failed";
    private readonly HashSet<int> _retiredWaits = [];
    private readonly HashSet<string> _retiredDeferred = new(StringComparer.OrdinalIgnoreCase);
    private string? _retiredShown;

    internal Func<string, string, Task> RecycleAddonFolder { get; set; } = AddonRecycler.RecycleAsync;

    private string InstallLabel(string flavourPath) =>
        Installs.FirstOrDefault(install => string.Equals(install.FlavourPath, flavourPath, StringComparison.OrdinalIgnoreCase))?.Label ?? flavourPath;

    private void SaveRetiredAddons(IReadOnlyList<RetiredAddon>? retired)
    {
        var state = _stateStore.Load();
        var listed = RetiredAddons.Listed(retired);
        var declines = RetiredAddons.PruneDeclines(state.DeclinedRetiredAddons ?? [], listed);
        if (!(state.RetiredAddons ?? []).SequenceEqual(listed ?? []) || declines.Count != (state.DeclinedRetiredAddons?.Count ?? 0))
        {
            _stateStore.Save(state with { RetiredAddons = listed, DeclinedRetiredAddons = declines });
        }
    }

    private List<RetiredFolder> FindRetiredAddons() =>
    [
        .. RetiredAddons.Find(
            _stateStore.Load(),
            Installs.Where(install => !install.Install.IsMissing).Select(install => new RetiredInstall(
                install.FlavourPath,
                [
                    .. ExcludedFoldersForOthers(install, string.Empty),
                    .. _addons.Concat(_addonCatalogue ?? []).SelectMany(addon => (addon.Folders ?? []).Append(addon.FolderName)),
                ]))),
    ];

    private void ShowRetiredAddons()
    {
        var found = FindRetiredAddons().Where(entry => !_retiredDeferred.Contains(RetiredAddons.DeclineKey(entry))).ToList();
        var shown = string.Join("\n", found.Select(RetiredAddons.DeclineKey));
        if (shown == _retiredShown)
        {
            return;
        }

        _retiredShown = shown;
        _localBanners.RemoveAll(banner => banner.Id == RetiredBannerId);
        if (found.Count == 0)
        {
            RefreshBanners();
            return;
        }

        var keys = found.Select(RetiredAddons.DeclineKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ShowLocalInfoBanner(
            RetiredAddons.Prompt(found, InstallLabel),
            RetiredBannerId,
            "Uninstall",
            () => _ = UninstallRetiredAddonsAsync(keys),
            () =>
            {
                var state = _stateStore.Load();
                _stateStore.Save(state with { DeclinedRetiredAddons = [.. (state.DeclinedRetiredAddons ?? []).Union(keys, StringComparer.OrdinalIgnoreCase)] });
                _retiredShown = null;
            },
            RetiredAddons.Reasons(found));
    }

    private async Task UninstallRetiredAddonsAsync(HashSet<string> keys)
    {
        _localBanners.RemoveAll(banner => banner.Id is RetiredBannerId or RetiredDeferredBannerId or RetiredFailedBannerId);
        RefreshBanners();
        var running = new List<WowClientProcess>();
        var errors = new List<(RetiredFolder Entry, Exception Error)>();
        var (removed, deferred) = await RetiredAddons.RemoveAsync(
            [.. FindRetiredAddons().Where(entry => keys.Contains(RetiredAddons.DeclineKey(entry)))],
            flavourPath =>
            {
                if (WowClient.Find(flavourPath, WowClient.Snapshot()) is { } client)
                {
                    running.Add(client);
                    return true;
                }

                return false;
            },
            (entry, ex) => errors.Add((entry, ex)),
            RecycleAddonFolder).ConfigureAwait(true);
        var failures = new List<string>();
        foreach (var (entry, ex) in errors)
        {
            _logger.Warn(ex, $"Could not uninstall retired {entry.Folder} from {entry.FlavourPath}");
            failures.Add(RetiredAddons.Failed(entry, InstallLabel(entry.FlavourPath), ex));
        }

        foreach (var entry in removed)
        {
            _logger.Info($"Uninstalled retired {entry.Folder} from {entry.FlavourPath}");
        }

        _retiredDeferred.ExceptWith(keys);
        _retiredDeferred.UnionWith(deferred.Select(RetiredAddons.DeclineKey));
        if (deferred.Count > 0)
        {
            ShowLocalInfoBanner(RetiredAddons.Deferred(deferred), RetiredDeferredBannerId);
        }

        if (failures.Count > 0)
        {
            ShowLocalInfoBanner(string.Join(" ", failures), RetiredFailedBannerId);
        }

        foreach (var install in Installs.Where(install => removed.Any(entry => string.Equals(entry.FlavourPath, install.FlavourPath, StringComparison.OrdinalIgnoreCase))))
        {
            _ = install.RescanLocalAsync();
        }

        foreach (var client in running.Distinct())
        {
            if (_retiredWaits.Add(client.ProcessId))
            {
                _ = UninstallRetiredAfterExitAsync(client.ProcessId);
            }
        }

        _retiredShown = null;
        ShowRetiredAddons();
    }

    private async Task UninstallRetiredAfterExitAsync(int processId)
    {
        try
        {
            await WowClient.WaitForExitAsync(processId, CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            _retiredWaits.Remove(processId);
        }

        if (_retiredDeferred.Count > 0)
        {
            await UninstallRetiredAddonsAsync(_retiredDeferred.ToHashSet(StringComparer.OrdinalIgnoreCase)).ConfigureAwait(true);
        }
    }
}
