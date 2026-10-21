using System.ComponentModel;
using System.Diagnostics;

using CommunityToolkit.Mvvm.Input;

using Steward.Core;
using Steward.Core.Diagnostics;

namespace Steward.App.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly Lazy<string?> BattleNetExe = new(() => OperatingSystem.IsWindows() ? BattleNet.Launcher() : null);

    public bool LaunchGameVisibility =>
        SelectedInstall is { IsMissing: false, Install.ProductCode: not null } && BattleNetExe.Value is not null;

    [RelayCommand]
    private void LaunchGame()
    {
        if (BattleNetExe.Value is not { } exe || SelectedInstall?.Install.ProductCode is not { } product)
        {
            return;
        }

        _logger.Info($"Launching {product} through Battle.net");
        try
        {
            // launch_uid selects this product and brings Battle.net forward; no exec command presses Play (Battle.net 2.53.5, 20 Oct 2026).
            Process.Start(new ProcessStartInfo(exe, [$"--exec=launch_uid {product}"]))?.Dispose();
        }
        catch (Win32Exception ex)
        {
            _logger.Warn(ex, "Could not start Battle.net");
            StatusMessage = $"Could not start Battle.net: {ex.Message}";
        }
    }
}
