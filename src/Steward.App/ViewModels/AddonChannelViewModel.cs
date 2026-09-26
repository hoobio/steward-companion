using CommunityToolkit.Mvvm.ComponentModel;

using Steward.Core;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Steward.App.ViewModels;

public sealed partial class AddonChannelViewModel : ObservableObject
{
    private readonly ManagedAddon _addon;
    private readonly AppStateStore _stateStore;
    private readonly Action<string, string> _channelChanged;

    private AddonChannelStatus? _status;
    private bool _isApplying;

    public AddonChannelViewModel(ManagedAddon addon, AppStateStore stateStore, Action<string, string> channelChanged)
    {
        ArgumentNullException.ThrowIfNull(addon);

        _addon = addon;
        _stateStore = stateStore;
        _channelChanged = channelChanged;
        Icon = new BitmapImage(addon.IconUri);
        SelectedIndex = -1;
    }

    public string AddonId => _addon.Id;

    public string Name => _addon.DisplayName;

    public ImageSource Icon { get; }

    [ObservableProperty]
    public partial string Description { get; set; } = "No releases yet";

    [ObservableProperty]
    public partial int SelectedIndex { get; set; }

    [ObservableProperty]
    public partial bool Option1Enabled { get; set; }

    [ObservableProperty]
    public partial bool Option2Enabled { get; set; }

    [ObservableProperty]
    public partial bool Option3Enabled { get; set; }

    [ObservableProperty]
    public partial string? Option1Tooltip { get; set; }

    [ObservableProperty]
    public partial string? Option2Tooltip { get; set; }

    [ObservableProperty]
    public partial string? Option3Tooltip { get; set; }

    [ObservableProperty]
    public partial string Option1Label { get; set; } = "";

    [ObservableProperty]
    public partial string Option2Label { get; set; } = "";

    [ObservableProperty]
    public partial string Option3Label { get; set; } = "";

    [ObservableProperty]
    public partial Visibility Option2Visibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility Option3Visibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility PickerVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility NoReleasesVisibility { get; set; } = Visibility.Visible;

    public void Apply(AddonChannelStatus status, bool isGlobalAdmin)
    {
        ArgumentNullException.ThrowIfNull(status);

        _isApplying = true;
        try
        {
            _status = status;
            var channels = _addon.Channels;
            Option1Label = channels.Count > 0 ? channels[0] : "";
            Option2Label = channels.Count > 1 ? channels[1] : "";
            Option3Label = channels.Count > 2 ? channels[2] : "";
            Option1Enabled = channels.Count > 0 && status.Has(channels[0]);
            Option2Enabled = channels.Count > 1 && status.Has(channels[1]);
            Option3Enabled = channels.Count > 2 && status.Has(channels[2]);
            Option1Tooltip = channels.Count > 0 && !Option1Enabled ? $"No releases on {channels[0]} yet" : null;
            Option2Tooltip = channels.Count > 1 && !Option2Enabled ? $"No releases on {channels[1]} yet" : null;
            Option3Tooltip = channels.Count > 2 && !Option3Enabled ? $"No releases on {channels[2]} yet" : null;
            Option2Visibility = channels.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            Option3Visibility = channels.Count > 2 && isGlobalAdmin ? Visibility.Visible : Visibility.Collapsed;

            SelectedIndex = status.Channel is null ? -1 : IndexOf(status.Channel);
            Description = status.Channel is null
                ? "No releases yet"
                : $"{status.Channel}, {status.Release?.Version}";

            var resolved = status.Channel is not null;
            NoReleasesVisibility = resolved ? Visibility.Collapsed : Visibility.Visible;
            var choices = (Option1Enabled ? 1 : 0)
                + (Option2Visibility == Visibility.Visible && Option2Enabled ? 1 : 0)
                + (Option3Visibility == Visibility.Visible && Option3Enabled ? 1 : 0);
            PickerVisibility = resolved && choices > 1 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _isApplying = false;
        }
    }

    private int IndexOf(string channel)
    {
        var channels = _addon.Channels;
        for (var i = 0; i < channels.Count; i++)
        {
            if (string.Equals(channels[i], channel, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (_isApplying || value < 0 || value >= _addon.Channels.Count)
        {
            return;
        }

        var channel = _addon.Channels[value];
        if (_status is null || string.Equals(_status.Channel, channel, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var state = _stateStore.Load();
        state.Channels[AddonId] = channel;
        _stateStore.Save(state);
        _channelChanged(AddonId, channel);
    }
}
