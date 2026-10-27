using System.Collections.ObjectModel;
using System.Text.Json;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using Steward.App.Services;
using Steward.Core;
using Steward.Core.Diagnostics;

namespace Steward.App.ViewModels;

public sealed partial class AddonSettingsViewModel : ObservableObject
{
    public const string AddonId = "hoobiscripts";

    public const string NoAccountMessage = "Log in to World of Warcraft once to create an account folder";

    public const string ExportDescriptionText = "Opens guild.hoobi.io with this account's addon settings, talent plans and action bar plans";

    public const string EmptyClipboardMessage = "Copy the link on the website first";

    private const int MaxQueued = 4;

    private readonly MainViewModel _main;
    private readonly ILogger _logger;
    private readonly Queue<string> _queued = new();

    internal AddonSettingsViewModel(MainViewModel main, AddonUpdater updater, IUiPlatform platform, ILogger logger)
    {
        _main = main;
        _logger = logger;
        FetchSchema = updater.GetSettingsSchemaAsync;
        ReadClipboard = platform.GetClipboardTextAsync;
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.SelectedInstall) or nameof(MainViewModel.AddonSettingsVisibility))
            {
                RefreshAccounts();
            }
        };
    }

    internal Func<ManagedAddon, string, CancellationToken, Task<SettingsSchema?>> FetchSchema { get; set; }

    internal Func<WowInstall, bool> IsRunning { get; set; } = WowClient.IsRunning;

    internal Func<Task<string?>> ReadClipboard { get; set; }

    internal Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    public Func<SettingsImport, string, Task<IReadOnlyList<SettingsImportItem>?>>? ShowImport { get; set; }

    public ObservableCollection<ForeverCopyAccount> Accounts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExportDescription), nameof(CanExport))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial ForeverCopyAccount? SelectedAccount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand), nameof(PasteCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExportDescription))]
    public partial string? Status { get; set; }

    public string ExportDescription => Status ?? (SelectedAccount is null ? NoAccountMessage : ExportDescriptionText);

    public void RefreshAccounts()
    {
        var previous = SelectedAccount?.Folder;
        Accounts.Clear();
        if (_main.SelectedInstall is { IsMissing: false } install)
        {
            foreach (var account in ForeverCopy.Accounts(install.Install))
            {
                Accounts.Add(account);
            }
        }

        SelectedAccount = Accounts.FirstOrDefault(account => string.Equals(account.Folder, previous, StringComparison.OrdinalIgnoreCase)) ?? Accounts.FirstOrDefault();
    }

    public bool CanExport => !IsBusy && SelectedAccount is not null;

    private bool CanPaste => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync(string? view)
    {
        if (_main.SelectedInstall is not { } install || SelectedAccount is not { } account)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await ExportAsync(install, account.Folder, view).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }

        await RunQueuedAsync().ConfigureAwait(true);
    }

    private async Task ExportAsync(WowInstallViewModel install, string account, string? view)
    {
        try
        {
            var (settings, sources) = await LoadAsync(install, account).ConfigureAwait(true);
            var json = await Task.Run(() => AddonSettingsExport.Build(settings, sources, install.GameVersionName ?? install.Label, Now().ToUnixTimeSeconds())).ConfigureAwait(true);
            var url = AddonSettingsPayload.ExportUrl(AddonSettingsPayload.Encode(json), view);
            if (url.Length > AddonSettingsPayload.MaxExportUrlLength)
            {
                _logger.Info($"Addon settings export of {url.Length} characters refused");
                Status = AddonSettingsPayload.TooLarge;
                return;
            }

            _logger.Info($"Addon settings export opened ({url.Length} characters, {sources.Count} addons)");
            Status = null;
            ShellOpen.Open(url);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            _logger.Warn(ex, "Addon settings export failed");
            Status = $"Could not export: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private async Task PasteAsync()
    {
        var text = await ReadClipboard().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(text))
        {
            Status = EmptyClipboardMessage;
            return;
        }

        await ImportAsync(text).ConfigureAwait(true);
    }

    public async Task ReceiveLinkAsync(string link)
    {
        ArgumentNullException.ThrowIfNull(link);
        _logger.Info($"Addon settings import link received ({link.Length} characters)");
        if (_main.InitializeCommand.ExecutionTask is { } initializing)
        {
            await initializing.ConfigureAwait(true);
        }

        await ImportAsync(link).ConfigureAwait(true);
    }

    private void Report(string message)
    {
        Status = message;
        _main.ShowToast(message);
    }

    private async Task ImportAsync(string text)
    {
        if (IsBusy)
        {
            if (_queued.Count >= MaxQueued)
            {
                Report("Too many import links at once; send this one again");
                return;
            }

            _queued.Enqueue(text);
            _logger.Info($"Addon settings import queued behind the one in progress ({_queued.Count} waiting)");
            return;
        }

        IsBusy = true;
        try
        {
            await ImportOneAsync(text).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warn(ex, "Addon settings import failed");
            Report(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        await RunQueuedAsync().ConfigureAwait(true);
    }

    private async Task RunQueuedAsync()
    {
        if (!IsBusy && _queued.TryDequeue(out var next))
        {
            await ImportAsync(next).ConfigureAwait(true);
        }
    }

    private async Task ImportOneAsync(string text)
    {
        Status = null;
        SettingsImportPayload payload;
        try
        {
            payload = AddonSettingsImport.Parse(AddonSettingsPayload.Decode(AddonSettingsPayload.FromLink(text)));
        }
        catch (FormatException ex)
        {
            _logger.Info($"Addon settings import refused: {ex.Message}");
            Report(ex.Message);
            return;
        }

        var install = _main.Installs.FirstOrDefault(candidate => !candidate.IsMissing
            && candidate.Install.ProductCode == payload.Product
            && Directory.Exists(AddonSettings.AccountFolder(candidate.FlavourPath, payload.Account)));
        if (install is null)
        {
            Report($"No World of Warcraft install here has the account {payload.Account}");
            return;
        }

        if (IsRunning(install.Install))
        {
            Report(AddonSettings.RunningMessage);
            return;
        }

        var (settings, sources) = await LoadAsync(install, payload.Account).ConfigureAwait(true);
        var preview = await Task.Run(() => AddonSettingsImport.Preview(payload, settings, sources)).ConfigureAwait(true);
        _logger.Info($"Addon settings import for {install.FlavourPath}: {preview.Items.Count(item => item.Status == SettingsImportStatus.Ready)} ready, "
            + $"{preview.Items.Count(item => item.Status == SettingsImportStatus.Conflict)} changed in game, {preview.Items.Count(item => item.Status == SettingsImportStatus.Refused)} refused");
        if (ShowImport is not { } show || await show(preview, $"{install.Label} · {payload.Account}").ConfigureAwait(true) is not { } chosen)
        {
            _logger.Info("Addon settings import cancelled");
            return;
        }

        if (IsRunning(install.Install))
        {
            Report(AddonSettings.RunningMessage);
            return;
        }

        var applied = await Task.Run(() => AddonSettingsImport.Apply(preview, settings, sources, chosen, () => IsRunning(install.Install))).ConfigureAwait(true);
        _logger.Info($"Addon settings import applied {applied} changes to {install.FlavourPath}");
        if (_main.ShowLinkDialog is { } done
            && await done(
                "Addon settings applied",
                $"Applied {applied} {(applied == 1 ? "change" : "changes")} to {payload.Account}. The game reads them at the next login.",
                "Export again",
                "Close").ConfigureAwait(true))
        {
            await ExportAsync(install, payload.Account, null).ConfigureAwait(true);
        }
    }

    private async Task<(SettingsAccount Settings, IReadOnlyList<SettingsSource> Sources)> LoadAsync(WowInstallViewModel install, string account)
    {
        var sources = new List<SettingsSource>();
        var rows = install.AddonRows.Where(IsSettingsAddon).OrderBy(row => string.Equals(row.AddonId, AddonId, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ToList();
        foreach (var row in rows)
        {
            if (await LoadSchemaAsync(row).ConfigureAwait(true) is { } source)
            {
                sources.Add(source);
            }
        }

        var folders = rows.Select(row => row.Addon.FolderName).ToList();
        var clientInterface = TocFile.InterfaceNumber(install.ClientVersion);
        var settings = await Task.Run(() => new SettingsAccount(
            install.FlavourPath,
            install.Install.AddOnsPath,
            account,
            install.Install.ProductCode,
            AddonSettings.SavedVariableOwners(install.Install.AddOnsPath, folders, clientInterface))).ConfigureAwait(true);
        return (settings, sources);
    }

    // Every addon built from hoobio/HoobiScripts is a Steward or Protected catalogue row; one without a settings page answers 404 and is left out.
    private static bool IsSettingsAddon(AddonRowViewModel row) =>
        row.IsInstalled
        && !row.Addon.IsCurseForge
        && row.Addon.Source is AddonCatalogue.StewardSource or AddonCatalogue.ProtectedSource;

    private async Task<SettingsSource?> LoadSchemaAsync(AddonRowViewModel row)
    {
        var version = row.InstalledVersion!;
        try
        {
            if (await FetchSchema(row.Addon, version, CancellationToken.None).ConfigureAwait(true) is not { } schema)
            {
                return null;
            }

            if (schema.Refusal(row.Addon.FolderName) is { } reason)
            {
                _logger.Warn(null, $"Settings schema for {row.AddonId} {version} refused: {reason}");
                return new SettingsSource(row.AddonId, row.Addon.FolderName, version, null);
            }

            return new SettingsSource(row.AddonId, row.Addon.FolderName, version, schema);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or SessionExpiredException or InvalidOperationException)
        {
            _logger.Warn(null, $"Settings schema for {row.AddonId} {version} could not be loaded: {ex.Message}");
            return new SettingsSource(row.AddonId, row.Addon.FolderName, version, null);
        }
    }
}
