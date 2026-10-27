using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Steward.Core;

namespace Steward.App.ViewModels;

public abstract partial class SettingRowViewModel : ObservableObject
{
    public const string UpdateStewardNotice = "Update Steward to change this setting";

    protected SettingRowViewModel(SettingsRow row, string file, LuaValue original, bool isReadOnly)
    {
        Row = row;
        File = file;
        Original = original;
        IsReadOnly = isReadOnly || !row.IsKnownType;
    }

    public SettingsRow Row { get; }

    public string File { get; }

    public string? ReadOnlyNotice { get; init; }

    public string Addon { get; internal set; } = "";

    internal bool IsLoadedShown { get; set; } = true;

    public LuaValue Original { get; }

    public bool IsReadOnly { get; }

    public bool IsLocked { get; init; }

    public string Label => Row.Label ?? Row.Key ?? "";

    public bool IsIndented => Row.Parent is not null;

    public virtual bool IsSetting => Row.IsSetting && !IsReadOnly && !IsLocked;

    public abstract LuaValue Value { get; }

    public bool IsDirty => IsSetting && Error is null && !AddonSettings.Same(Value, Original);

    public string? Description => Error ?? (IsReadOnly ? ReadOnlyNotice ?? UpdateStewardNotice : IsLocked ? LockedNotice : Row.Tooltip);

    private string LockedNotice => $"Off while {string.Join(" or ", (Row.LockedWhenLoaded ?? []).Select(name => name.TrimEnd('*')))} is installed";

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsShown { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description), nameof(IsDirty))]
    public partial string? Error { get; set; }

    internal Action? Changed { get; set; }

    protected void OnValueChanged()
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IsDirty));
        Changed?.Invoke();
    }
}

public sealed partial class ToggleSettingViewModel : SettingRowViewModel
{
    public ToggleSettingViewModel(SettingsRow row, string file, LuaValue original, bool isReadOnly)
        : base(row, file, original, isReadOnly) => IsOn = original.Kind == LuaKind.Boolean && original.Boolean;

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    public override LuaValue Value => LuaValue.FromBoolean(IsOn);

    partial void OnIsOnChanged(bool value) => OnValueChanged();
}

public sealed partial class ChoiceSettingViewModel : SettingRowViewModel
{
    private readonly List<LuaValue> _values;

    public ChoiceSettingViewModel(SettingsRow row, string file, LuaValue original, bool isReadOnly, IReadOnlyList<SettingsOption> options)
        : base(row, file, original, isReadOnly)
    {
        Options = [.. options.Select(option => option.Label)];
        _values = [.. options.Select(option => AddonSettings.ToLua(option.Value))];
        SelectedIndex = _values.FindIndex(value => AddonSettings.Same(value, original));
    }

    public IReadOnlyList<string> Options { get; }

    [ObservableProperty]
    public partial int SelectedIndex { get; set; }

    public override LuaValue Value => SelectedIndex >= 0 && SelectedIndex < _values.Count ? _values[SelectedIndex] : Original;

    partial void OnSelectedIndexChanged(int value) => OnValueChanged();
}

public sealed partial class BitOptionViewModel(string label, int bit, bool isChecked, Action changed) : ObservableObject
{
    public string Label { get; } = label;

    public int Bit { get; } = bit;

    [ObservableProperty]
    public partial bool IsChecked { get; set; } = isChecked;

    partial void OnIsCheckedChanged(bool value) => changed();
}

public sealed class BitmaskSettingViewModel : SettingRowViewModel
{
    private readonly long _original;

    public BitmaskSettingViewModel(SettingsRow row, string file, LuaValue original, bool isReadOnly)
        : base(row, file, original, isReadOnly)
    {
        _original = original.Kind == LuaKind.Number ? (long)original.Number : 0;
        Bits = [.. (row.Options ?? []).Where(option => option.Bit is >= 0 and < 53)
            .Select(option => new BitOptionViewModel(option.Label, option.Bit!.Value, (_original & (1L << option.Bit!.Value)) != 0, OnValueChanged))];
    }

    public ObservableCollection<BitOptionViewModel> Bits { get; }

    public override LuaValue Value => LuaValue.FromNumber(Bits.Aggregate(_original, (mask, bit) => bit.IsChecked ? mask | (1L << bit.Bit) : mask & ~(1L << bit.Bit)));
}

public sealed partial class TextSettingViewModel : SettingRowViewModel
{
    private LuaValue? _parsed;

    public TextSettingViewModel(SettingsRow row, string file, LuaValue original, bool isReadOnly)
        : base(row, file, original, isReadOnly)
    {
        IsMoney = row.Type == "money";
        Text = original.Kind switch
        {
            LuaKind.Number when IsMoney => AddonSettings.FormatMoney(original.Number),
            LuaKind.Number => original.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            LuaKind.Text => original.Text!,
            _ => "",
        };
        _parsed = original;
    }

    public bool IsMoney { get; }

    public string? Placeholder => Row.Placeholder ?? (IsMoney ? "0g 0s 0c" : null);

    public int MaxLength => Row.MaxLetters ?? 0;

    [ObservableProperty]
    public partial string Text { get; set; }

    public override LuaValue Value => _parsed ?? Original;

    partial void OnTextChanged(string value)
    {
        if (IsMoney)
        {
            var copper = AddonSettings.ParseMoney(value);
            _parsed = copper is { } amount ? LuaValue.FromNumber(amount) : null;
            Error = copper is null ? "Enter an amount such as 1g 20s 5c" : null;
        }
        else
        {
            Error = AddonSettings.Check(Row, value, out var parsed);
            _parsed = parsed;
        }

        OnValueChanged();
    }
}

public sealed partial class MailTargetSettingViewModel : SettingRowViewModel
{
    public MailTargetSettingViewModel(SettingsRow row, string file, LuaValue original, bool isReadOnly)
        : base(row, file, original, isReadOnly)
    {
        var text = original.Kind == LuaKind.Text ? original.Text! : "";
        var mode = text switch
        {
            "" => 0,
            AddonSettings.NeverMailTarget => 1,
            _ => 2,
        };
        Name = mode == 2 ? text : "";
        ModeIndex = mode;
    }

    public static IReadOnlyList<string> Modes { get; } = ["Default", "Never", "Character"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNameEnabled))]
    public partial int ModeIndex { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    public bool IsNameEnabled => ModeIndex == 2;

    public override LuaValue Value => ModeIndex switch
    {
        0 => LuaValue.FromString(""),
        1 => LuaValue.FromString(AddonSettings.NeverMailTarget),
        _ => LuaValue.FromString(Name.Trim()),
    };

    partial void OnModeIndexChanged(int value) => Validate();

    partial void OnNameChanged(string value) => Validate();

    private void Validate()
    {
        Error = ModeIndex == 2
            ? Name.Trim().Length == 0 ? "Enter a character name" : AddonSettings.Check(Row, Name, out _)
            : null;
        OnValueChanged();
    }
}

public sealed class NoteSettingViewModel(SettingsRow row, string file) : SettingRowViewModel(row, file, LuaValue.Nil, isReadOnly: false)
{
    public string Text => Row.Text ?? Row.Label ?? "";

    public override bool IsSetting => false;

    public override LuaValue Value => LuaValue.Nil;
}

public sealed class ReadOnlySettingViewModel(SettingsRow row, string file, LuaValue original, string shown) : SettingRowViewModel(row, file, original, isReadOnly: true)
{
    public string Shown => shown;

    public override LuaValue Value => Original;
}
