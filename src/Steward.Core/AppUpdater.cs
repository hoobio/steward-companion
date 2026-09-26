using System.Diagnostics;
using System.Text;

namespace Steward.Core;

public sealed class AppUpdater(string storeProductId)
{
    public Uri StoreListingUri { get; } = new($"ms-windows-store://pdp/?productid={storeProductId}");

    private static string EscapeSingleQuoted(string value) => value.Replace("'", "''");

    public static void SwitchToStoreAfterExit(string upgradeCode, string logPath, string appUserModelId) =>
        RunAfterExit(SwitchToStoreScript(upgradeCode, logPath, appUserModelId));

    public static string SwitchToStoreScript(string upgradeCode, string logPath, string appUserModelId)
    {
        var escapedUpgradeCode = EscapeSingleQuoted(upgradeCode);
        var escapedLogPath = EscapeSingleQuoted(logPath);
        var escapedAppUserModelId = EscapeSingleQuoted(appUserModelId);
        return $$"""
            foreach ($productCode in (New-Object -ComObject WindowsInstaller.Installer).RelatedProducts('{{escapedUpgradeCode}}')) {
                $exitCode = (Start-Process msiexec.exe -ArgumentList '/x', $productCode, '/qn', '/l*v', '"{{escapedLogPath}}"' -PassThru -Wait).ExitCode
                if ($exitCode -ne 0 -and $exitCode -ne 3010) { exit $exitCode }
            }
            Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Steward' -ErrorAction SilentlyContinue
            Start-Process explorer.exe -ArgumentList 'shell:AppsFolder\{{escapedAppUserModelId}}'
            """;
    }

    private static void RunAfterExit(string body)
    {
        var script = $"""
            Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue
            {body}
            """;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })?.Dispose();
    }
}
