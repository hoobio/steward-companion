using System.ComponentModel;
using System.Diagnostics;

using CommunityToolkit.Mvvm.Input;

using Steward.Core;
using Steward.Core.Diagnostics;

namespace Steward.App.ViewModels;

public sealed partial class MainViewModel
{
    private const string ForeverFamily = "WoWF";

    private static readonly Lazy<string?> BattleNetExe = new(() => OperatingSystem.IsWindows() ? BattleNet.Launcher() : null);

    private static readonly Lazy<IReadOnlySet<string>> ForeverDefaults = new(() => OperatingSystem.IsWindows() ? BattleNet.FamilyDefaults(BattleNet.CacheFolder, ForeverFamily) : new HashSet<string>());

    public bool LaunchGameVisibility =>
        SelectedInstall is { IsMissing: false, Install.ProductCode: not null } && BattleNetExe.Value is not null;

    [RelayCommand]
    private void LaunchGame()
    {
        if (BattleNetExe.Value is not { } exe || SelectedInstall?.Install.ProductCode is not { } product)
        {
            return;
        }

        var otherInstalled = Installs.Select(install => install.Install.ProductCode).OfType<string>().Where(code => code != product).ToHashSet(StringComparer.Ordinal);
        _ = Task.Run(() =>
        {
            // launch_uid only selects a product; launch <family> presses Play but always on the family's default product, so it is sent only when that default is the selected one (Battle.net 2.53.5, 21 Oct 2026).
            var defaults = ForeverDefaults.Value;
            var plays = defaults.Contains(product) && !defaults.Any(otherInstalled.Contains);
            _logger.Info(plays
                ? $"Launching {product} through Battle.net"
                : $"Selecting {product} in Battle.net; {ForeverFamily} defaults to {(defaults.Count == 0 ? "an unknown product" : string.Join(", ", defaults))}, so Play is left to the user");
            try
            {
                foreach (var command in plays ? [$"--exec=launch_uid {product}", $"--exec=launch {ForeverFamily}"] : new[] { $"--exec=launch_uid {product}" })
                {
                    using var process = Process.Start(new ProcessStartInfo(exe, [command]));
                    process?.WaitForExit(5000);
                }
            }
            catch (Win32Exception ex)
            {
                _logger.Warn(ex, "Could not start Battle.net");
                _platform.TryEnqueue(() => StatusMessage = $"Could not start Battle.net: {ex.Message}");
            }
        });
    }
}
