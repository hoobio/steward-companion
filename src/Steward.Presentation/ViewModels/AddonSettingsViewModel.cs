using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.Core;

namespace Steward.App.ViewModels;

public sealed record SettingsCharacterOption(string Key, string Label);

public sealed record AddonSettingsGroupViewModel(string? Title, IReadOnlyList<SettingRowViewModel> Rows);

public sealed record AddonSettingsPageViewModel(string Title, string? Message, IReadOnlyList<AddonSettingsGroupViewModel> Groups);

public sealed partial class AddonSettingsViewModel : ObservableObject
{
    public const string AddonId = "hoobiscripts";

    public const string CoreFolder = "HoobiScripts";

    public const string NewCharacterUnavailable = "Log in to a character on this account once first";

    public const string NotAvailableMessage = "Settings are not available for this version";

    private readonly MainViewModel _main;
    private readonly List<(AddonRowViewModel Row, SettingsSchema? Schema, string? Message)> _schemas = [];
    private readonly List<SettingRowViewModel> _rows = [];
    private IReadOnlyDictionary<string, LuaValue> _core = new Dictionary<string, LuaValue>();
    private WowInstallViewModel? _install;
    private bool _isActive;
    private bool _rebuilding;
    private int _generation;

    internal AddonSettingsViewModel(MainViewModel main, AddonUpdater updater)
    {
        _main = main;
        FetchSchema = updater.GetSettingsSchemaAsync;
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.SelectedInstall) && _isActive)
            {
                _ = LoadAsync();
            }
        };
    }

    internal Func<ManagedAddon, string, CancellationToken, Task<SettingsSchema?>> FetchSchema { get; set; }

    internal Func<WowInstall, bool> IsRunning { get; set; } = WowClient.IsRunning;

    public ObservableCollection<ForeverCopyAccount> Accounts { get; } = [];

    public ObservableCollection<SettingsCharacterOption> Characters { get; } = [];

    public ObservableCollection<AddonSettingsPageViewModel> Pages { get; } = [];

    public IReadOnlyDictionary<string, string> KnownRealms { get; private set; } = new Dictionary<string, string>();

    [ObservableProperty]
    public partial ForeverCopyAccount? SelectedAccount { get; set; }

    [ObservableProperty]
    public partial SettingsCharacterOption? SelectedCharacter { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateCharacter), nameof(RunningVisibility))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsClientRunning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MessageVisibility))]
    public partial string? Message { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusVisibility))]
    public partial string? Status { get; set; }

    public bool MessageVisibility => Message is not null;

    public bool StatusVisibility => Status is not null;

    public bool RunningVisibility => IsClientRunning;

    public bool CharacterPickerVisibility => _rows.Any(row => row.Row.IsCharacter);

    public bool CanCreateCharacter => !IsClientRunning && SelectedAccount is not null && KnownRealms.Count > 0;

    public string NewCharacterTip => KnownRealms.Count > 0 ? "Add settings for a character before its first login" : NewCharacterUnavailable;

    public bool HasChanges => _rows.Any(row => row.IsDirty);

    private bool CanSave => !IsClientRunning && HasChanges && !_rows.Any(row => row.IsShown && row.IsEnabled && row.Error is not null);

    public event EventHandler? PagesChanged;

    public Task ActivateAsync()
    {
        _isActive = true;
        return LoadAsync();
    }

    public void Deactivate() => _isActive = false;

    public async Task LoadAsync()
    {
        var generation = ++_generation;
        SetInstall(_main.SelectedInstall);
        _schemas.Clear();
        Status = null;
        if (_install is not { } install)
        {
            Accounts.Clear();
            Rebuild();
            return;
        }

        var previous = SelectedAccount?.Folder;
        _rebuilding = true;
        Accounts.Clear();
        foreach (var account in ForeverCopy.Accounts(install.Install))
        {
            Accounts.Add(account);
        }

        _rebuilding = false;
        IsClientRunning = install.IsClientRunning || IsRunning(install.Install);
        IsLoading = true;
        try
        {
            foreach (var row in install.AddonRows.Where(IsSettingsAddon).OrderBy(row => IsCore(row) ? 0 : 1))
            {
                var (schema, message) = await LoadSchemaAsync(row).ConfigureAwait(true);
                if (generation != _generation || _main.IsDisposed)
                {
                    return;
                }

                if (schema is not null || IsCore(row))
                {
                    _schemas.Add((row, schema, message));
                }
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoading = false;
            }
        }

        _rebuilding = true;
        SelectedAccount = Accounts.FirstOrDefault(account => string.Equals(account.Folder, previous, StringComparison.OrdinalIgnoreCase)) ?? Accounts.FirstOrDefault();
        _rebuilding = false;
        Rebuild();
    }

    private static bool IsCore(AddonRowViewModel row) => string.Equals(row.AddonId, AddonId, StringComparison.OrdinalIgnoreCase);

    // Every addon built from hoobio/HoobiScripts is a Steward or Protected catalogue row; one without a settings page answers 404 and is left out.
    private static bool IsSettingsAddon(AddonRowViewModel row) =>
        row.IsInstalled
        && !row.Addon.IsCurseForge
        && row.Addon.Source is AddonCatalogue.StewardSource or AddonCatalogue.ProtectedSource;

    private async Task<(SettingsSchema? Schema, string? Message)> LoadSchemaAsync(AddonRowViewModel row)
    {
        try
        {
            return await FetchSchema(row.Addon, row.InstalledVersion!, CancellationToken.None).ConfigureAwait(true) is { } schema
                ? (schema, null)
                : (null, NotAvailableMessage);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or SessionExpiredException or InvalidOperationException)
        {
            _main.WarnUi($"Settings schema for {row.AddonId} {row.InstalledVersion} could not be loaded: {ex.Message}");
            return (null, $"Could not load the settings: {ex.Message}");
        }
    }

    private void SetInstall(WowInstallViewModel? install)
    {
        if (_install is not null)
        {
            _install.PropertyChanged -= OnInstallPropertyChanged;
        }

        _install = install;
        if (install is not null)
        {
            install.PropertyChanged += OnInstallPropertyChanged;
        }
    }

    private void OnInstallPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WowInstallViewModel.IsClientRunning) && _install is { } install)
        {
            IsClientRunning = install.IsClientRunning;
        }
    }

    partial void OnIsClientRunningChanged(bool value) => RefreshStates();

    partial void OnSelectedAccountChanged(ForeverCopyAccount? value)
    {
        if (!_rebuilding)
        {
            Rebuild();
        }
    }

    partial void OnSelectedCharacterChanged(SettingsCharacterOption? value)
    {
        if (!_rebuilding)
        {
            Rebuild();
        }
    }

    private string? AccountFolder => _install is { } install && SelectedAccount is { } account
        ? AddonSettings.AccountFolder(install.FlavourPath, account.Folder)
        : null;

    private string FileFor(string folder) => AddonSettings.FilePath(_install!.FlavourPath, SelectedAccount!.Folder, folder);

    private void Rebuild()
    {
        _rows.Clear();
        Pages.Clear();
        Message = null;
        _core = new Dictionary<string, LuaValue>();
        if (_install is null || SelectedAccount is null)
        {
            Message = _install is null ? null : "Log in to World of Warcraft once to create an account folder";
            Finish([]);
            return;
        }

        var coreMessage = (string?)null;
        try
        {
            _core = AddonSettings.Load(FileFor(CoreFolder));
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            coreMessage = $"Steward could not read {CoreFolder}.lua: {ex.Message}";
        }

        var characters = AddonSettings.Characters(_core);
        KnownRealms = AddonSettings.KnownRealms(AccountFolder!, characters);
        var selected = SelectedCharacter?.Key;
        _rebuilding = true;
        Characters.Clear();
        foreach (var character in characters)
        {
            Characters.Add(new SettingsCharacterOption(character, AddonSettings.CharacterLabel(character)));
        }

        SelectedCharacter = Characters.FirstOrDefault(option => string.Equals(option.Key, selected, StringComparison.Ordinal)) ?? Characters.FirstOrDefault();
        _rebuilding = false;

        var pages = new List<AddonSettingsPageViewModel>();
        foreach (var (row, schema, message) in _schemas)
        {
            if (schema is null)
            {
                pages.Add(new AddonSettingsPageViewModel(row.DisplayName, message, []));
                continue;
            }

            var accountFile = FileFor(schema.Folder);
            var files = new Dictionary<string, (IReadOnlyDictionary<string, LuaValue> Globals, string? Message)>(StringComparer.OrdinalIgnoreCase)
            {
                [FileFor(CoreFolder)] = (_core, coreMessage),
            };
            var fileMessage = Read(files, accountFile).Message;
            foreach (var page in schema.Pages ?? [])
            {
                var groups = (page.Sections ?? [])
                    .Select(section => new AddonSettingsGroupViewModel(section.Title, [.. (section.Rows ?? []).Select(settingsRow => BuildRow(schema, settingsRow, accountFile, files))]))
                    .ToList();
                pages.Add(new AddonSettingsPageViewModel(page.Title, fileMessage ?? (schema.IsReadOnly ? SettingRowViewModel.UpdateStewardNotice : null), groups));
            }
        }

        Finish(pages);
    }

    private static (IReadOnlyDictionary<string, LuaValue> Globals, string? Message) Read(
        Dictionary<string, (IReadOnlyDictionary<string, LuaValue> Globals, string? Message)> files, string file)
    {
        if (files.TryGetValue(file, out var known))
        {
            return known;
        }

        try
        {
            return files[file] = (AddonSettings.Load(file), null);
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            return files[file] = (new Dictionary<string, LuaValue>(), $"Steward could not read {Path.GetFileName(file)}: {ex.Message}");
        }
    }

    private void Finish(List<AddonSettingsPageViewModel> pages)
    {
        foreach (var page in pages)
        {
            Pages.Add(page);
        }

        RefreshStates();
        OnPropertyChanged(nameof(CharacterPickerVisibility));
        OnPropertyChanged(nameof(CanCreateCharacter));
        OnPropertyChanged(nameof(NewCharacterTip));
        OnPropertyChanged(nameof(HasChanges));
        SaveCommand.NotifyCanExecuteChanged();
        PagesChanged?.Invoke(this, EventArgs.Empty);
    }

    private SettingRowViewModel BuildRow(
        SettingsSchema schema, SettingsRow row, string accountFile, Dictionary<string, (IReadOnlyDictionary<string, LuaValue> Globals, string? Message)> files)
    {
        var character = row.IsCharacter ? SelectedCharacter?.Key : null;
        var classToken = AddonSettings.ClassOf(_core, character);
        var file = accountFile;
        string? notice = null;
        if (row.IsCharacterFile)
        {
            var folder = character is null ? null : AddonSettings.CharacterFolder(AccountFolder!, character, KnownRealms);
            file = folder is null ? accountFile : AddonSettings.CharacterFilePath(folder, schema.Folder);
            notice = folder is null && character is not null ? "Log in to this character once first" : null;
        }
        else if (row.IsClass && character is not null && classToken is null)
        {
            notice = "Steward does not know this character's class yet";
        }

        var (globals, unreadable) = Read(files, file);
        notice ??= unreadable;
        var locked = AddonSettings.AnyInstalled(_install!.Install.AddOnsPath, row.LockedWhenLoaded);
        var value = locked ? LuaValue.FromBoolean(false) : AddonSettings.Resolve(globals, row, character, classToken);
        var readOnly = schema.IsReadOnly || notice is not null;
        var options = row.OptionsFor(classToken);
        SettingRowViewModel model = row.Type switch
        {
            _ when !row.IsKnownType => new ReadOnlySettingViewModel(row, file, value, Shown(value, options)),
            "dropdown" when row.DynamicOptions => new ReadOnlySettingViewModel(row, file, value, Shown(value, options)) { ReadOnlyNotice = "Set in the game" },
            "checkbox" => new ToggleSettingViewModel(row, file, value, readOnly) { IsLocked = locked, ReadOnlyNotice = notice },
            "dropdown" => new ChoiceSettingViewModel(row, file, value, readOnly, options) { IsLocked = locked, ReadOnlyNotice = notice },
            "bitmask" => new BitmaskSettingViewModel(row, file, value, readOnly) { IsLocked = locked, ReadOnlyNotice = notice },
            "text" or "money" => new TextSettingViewModel(row, file, value, readOnly) { IsLocked = locked, ReadOnlyNotice = notice },
            "mailTarget" => new MailTargetSettingViewModel(row, file, value, readOnly) { IsLocked = locked, ReadOnlyNotice = notice },
            _ => new NoteSettingViewModel(row, file),
        };
        model.Addon = schema.Folder;
        model.IsLoadedShown = row.VisibleWhenLoaded is null || AddonSettings.AnyInstalled(_install.Install.AddOnsPath, row.VisibleWhenLoaded);
        model.Changed = () =>
        {
            RefreshStates();
            OnPropertyChanged(nameof(HasChanges));
            SaveCommand.NotifyCanExecuteChanged();
            Status = null;
        };
        _rows.Add(model);
        return model;
    }

    private static string Shown(LuaValue value, IReadOnlyList<SettingsOption> options) =>
        options.FirstOrDefault(option => AddonSettings.Same(AddonSettings.ToLua(option.Value), value))?.Label
        ?? value.Kind switch
        {
            LuaKind.Text when value.Text!.Length > 0 => value.Text,
            LuaKind.Number => value.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            LuaKind.Boolean => value.Boolean ? "On" : "Off",
            _ => "Not set",
        };

    private void RefreshStates()
    {
        foreach (var model in _rows)
        {
            var parent = model.Row.Parent is { } key ? Sibling(model, key) : null;
            var condition = model.Row.VisibleWhen;
            model.IsShown = model.IsLoadedShown
                && (condition is null || Sibling(model, condition.Key) is not { } other || AddonSettings.Same(other.Value, AddonSettings.ToLua(condition.Value)));
            model.IsEnabled = !IsClientRunning
                && !model.IsReadOnly
                && !model.IsLocked
                && (!model.Row.IsCharacter || SelectedCharacter is not null)
                && (parent is null || (parent.IsEnabled && parent.Value is { Kind: LuaKind.Boolean, Boolean: true }));
        }
    }

    private SettingRowViewModel? Sibling(SettingRowViewModel model, string key) =>
        _rows.FirstOrDefault(other => other != model && string.Equals(other.Addon, model.Addon, StringComparison.OrdinalIgnoreCase) && other.Row.Key == key);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_install is not { } install || SelectedAccount is null)
        {
            return;
        }

        if (IsRunning(install.Install))
        {
            IsClientRunning = true;
            Status = AddonSettings.RunningMessage;
            return;
        }

        var character = SelectedCharacter?.Key;
        var changes = _rows.Where(row => row.IsDirty && row.IsEnabled).GroupBy(row => row.File, StringComparer.OrdinalIgnoreCase).ToList();
        var coreFile = FileFor(CoreFolder);
        try
        {
            await Task.Run(() =>
            {
                var core = AddonSettings.Load(coreFile);
                foreach (var group in changes)
                {
                    var file = group.Key;
                    var globals = AddonSettings.Apply(
                        AddonSettings.Load(file),
                        group.Select(row => new SettingChange(row.Row, row.Row.IsCharacter ? character : null, row.Value)),
                        key => AddonSettings.ClassOf(core, key));
                    AddonSettings.Write(file, globals, () => IsRunning(install.Install));
                }
            }).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or FormatException)
        {
            _main.WarnUi($"Addon settings save failed: {ex.Message}");
            Status = ex.Message;
            return;
        }

        Rebuild();
        Status = "Saved. The game reads these settings at the next login.";
    }

    public string? CreateCharacter(string first, string last, string? classToken, string realm)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(last);
        if (_install is not { } install || AccountFolder is not { } accountFolder || !KnownRealms.TryGetValue(realm, out var realmId))
        {
            return NewCharacterUnavailable;
        }

        if (classToken is null || !AddonSettings.ClassTokens.Contains(classToken))
        {
            return "Choose a class";
        }

        if (AddonSettings.CheckNamePart(first.Trim()) is { } firstError)
        {
            return $"First name: {firstError}";
        }

        if (AddonSettings.CheckNamePart(last.Trim()) is { } lastError)
        {
            return $"Last name: {lastError}";
        }

        var key = AddonSettings.CharacterKey(first, last, realm);
        if (Characters.Any(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase)))
        {
            return "That character already has settings";
        }

        if (IsRunning(install.Install))
        {
            IsClientRunning = true;
            return AddonSettings.RunningMessage;
        }

        try
        {
            var coreRows = _schemas.Where(entry => string.Equals(entry.Schema?.Folder, CoreFolder, StringComparison.OrdinalIgnoreCase))
                .SelectMany(entry => entry.Schema!.Pages ?? []).SelectMany(page => page.Sections ?? []).SelectMany(section => section.Rows ?? []);
            var file = FileFor(CoreFolder);
            var globals = AddonSettings.AddCharacter(AddonSettings.Load(file), key, classToken, coreRows);
            AddonSettings.CreateCharacterFolder(accountFolder, realmId, first, last);
            AddonSettings.Write(file, globals, () => IsRunning(install.Install));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or FormatException)
        {
            _main.WarnUi($"New character {key} failed: {ex.Message}");
            return ex.Message;
        }

        _rebuilding = true;
        SelectedCharacter = new SettingsCharacterOption(key, AddonSettings.CharacterLabel(key));
        _rebuilding = false;
        Rebuild();
        return null;
    }
}
