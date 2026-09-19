using CommunityToolkit.Mvvm.ComponentModel;

using Steward.Core;

using Microsoft.UI.Xaml;

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
        SelectedIndex = -1;
    }

    public string AddonId => _addon.Id;

    public string Name => _addon.FolderName;

    [ObservableProperty]
    public partial string Description { get; set; } = "No releases yet";

    [ObservableProperty]
    public partial int SelectedIndex { get; set; }

    [ObservableProperty]
    public partial bool StableEnabled { get; set; }

    [ObservableProperty]
    public partial bool BetaEnabled { get; set; }

    [ObservableProperty]
    public partial bool UnstableEnabled { get; set; }

    [ObservableProperty]
    public partial string? StableTooltip { get; set; }

    [ObservableProperty]
    public partial string? BetaTooltip { get; set; }

    [ObservableProperty]
    public partial string? UnstableTooltip { get; set; }

    [ObservableProperty]
    public partial Visibility UnstableVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility PickerVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility ReadOnlyVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility NoReleasesVisibility { get; set; } = Visibility.Visible;

    [ObservableProperty]
    public partial string SelectedChannelText { get; set; } = "No releases yet";

    public void Apply(AddonChannelStatus status, bool isGlobalAdmin, bool isAuthorized)
    {
        ArgumentNullException.ThrowIfNull(status);

        _isApplying = true;
        try
        {
            _status = status;
            StableEnabled = status.Has("stable");
            BetaEnabled = status.Has("beta");
            UnstableEnabled = status.Has("unstable");
            StableTooltip = StableEnabled ? null : "No releases on stable yet";
            BetaTooltip = BetaEnabled ? null : "No releases on beta yet";
            UnstableTooltip = UnstableEnabled ? null : "No releases on unstable yet";
            UnstableVisibility = isGlobalAdmin ? Visibility.Visible : Visibility.Collapsed;

            SelectedIndex = status.Channel is null ? -1 : IndexOf(status.Channel);
            SelectedChannelText = status.Channel ?? "No releases yet";
            Description = status.Channel is null
                ? "No releases yet"
                : $"{status.Channel}, {status.Release?.Version}";

            var resolved = status.Channel is not null;
            NoReleasesVisibility = resolved ? Visibility.Collapsed : Visibility.Visible;
            PickerVisibility = resolved && isAuthorized ? Visibility.Visible : Visibility.Collapsed;
            ReadOnlyVisibility = resolved && !isAuthorized ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _isApplying = false;
        }
    }

    private static int IndexOf(string channel)
    {
        for (var i = 0; i < AddonChannelStatus.Ordered.Count; i++)
        {
            if (string.Equals(AddonChannelStatus.Ordered[i], channel, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (_isApplying || value < 0 || value >= AddonChannelStatus.Ordered.Count)
        {
            return;
        }

        var channel = AddonChannelStatus.Ordered[value];
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
