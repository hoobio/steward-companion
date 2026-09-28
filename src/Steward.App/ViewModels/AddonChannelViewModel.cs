using Steward.App.Services;
using Steward.Core;

using Microsoft.UI.Xaml.Media;

namespace Steward.App.ViewModels;

public sealed record ChannelOption(string Channel, string Detail, bool IsEnabled, bool IsCurrent)
{
    public string Label => string.Concat(char.ToUpperInvariant(Channel[0]), Channel[1..]);
}

public sealed class AddonChannelViewModel
{
    private readonly ManagedAddon _addon;
    private readonly AppStateStore _stateStore;
    private readonly Action<string, string> _channelChanged;

    public AddonChannelViewModel(ManagedAddon addon, AppStateStore stateStore, Action<string, string> channelChanged)
    {
        ArgumentNullException.ThrowIfNull(addon);

        _addon = addon;
        _stateStore = stateStore;
        _channelChanged = channelChanged;
        Icon = ManifestIcon.For(addon);
    }

    public string AddonId => _addon.Id;

    public string Name => _addon.DisplayName;

    public string FolderName => _addon.FolderName;

    public ImageSource Icon { get; }

    public string? Current { get; private set; }

    public string Hint { get; set; } = "";

    public IReadOnlyList<ChannelOption> Options { get; private set; } = [];

    public void Apply(AddonChannelStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        Current = status.Channel;
        Options = [.. _addon.Channels.Select(channel => status.Releases.GetValueOrDefault(channel) is { } release
            ? new ChannelOption(channel, $"{release.Version} · {RelativeTime.Describe(release.Released, DateTimeOffset.Now)}", true, IsCurrent(channel))
            : new ChannelOption(channel, $"No releases on {channel} yet", false, IsCurrent(channel)))];
    }

    private bool IsCurrent(string channel) => string.Equals(Current, channel, StringComparison.OrdinalIgnoreCase);

    public void Save(string channel)
    {
        if (Current is null || IsCurrent(channel))
        {
            return;
        }

        var state = _stateStore.Load();
        state.Channels[AddonId] = channel;
        _stateStore.Save(state);
        _channelChanged(AddonId, channel);
    }
}
