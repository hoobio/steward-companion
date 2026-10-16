using System.Runtime.InteropServices;
using System.Text.Json;

using Steward.App.Services;
using Steward.Core;
using Steward.Core.Diagnostics;

namespace Steward.App.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly Lazy<LinuxInstall> LinuxInstall = new(() =>
        OperatingSystem.IsLinux() && Environment.ProcessPath is { } exe
            ? LinuxPackages.Detect(exe, ProcessCommand.Run)
            : new LinuxInstall(LinuxPackageKind.None, null));

    private static string? ReleaseAssetSuffix => RuntimeInformation.OSArchitecture switch
    {
        _ when OperatingSystem.IsLinux() => LinuxPackages.AssetSuffix(LinuxInstall.Value.Kind, RuntimeInformation.OSArchitecture),
        Architecture.Arm64 when OperatingSystem.IsMacOS() => "-macos-arm64.dmg",
        Architecture.X64 when OperatingSystem.IsMacOS() => "-macos-x64.dmg",
        Architecture.X64 when OperatingSystem.IsWindows() => "-win-x64.zip",
        _ => null,
    };

    // dpkg-query can take a moment, so the label reads the install kind only once a check has detected it off the UI thread.
    private static bool InstallsReleaseInApp =>
        AppInfo.UpdateSource == AppUpdateSource.ReleasePage && LinuxInstall.IsValueCreated && LinuxInstall.Value.Kind != LinuxPackageKind.None;

    private async Task<AddonRelease?> CheckReleaseUpdateAsync()
    {
        if (OperatingSystem.IsWindows() && !AppInfo.IsPortableZip)
        {
            return await CheckMsiUpdateAsync().ConfigureAwait(true);
        }

        _lastStoreCheck = DateTimeOffset.Now;
        await Task.Run(() => LinuxInstall.Value).ConfigureAwait(true);
        if (ReleaseAssetSuffix is not { } suffix)
        {
            _logger.Info($"Release update check: no package for {RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}");
            return null;
        }

        try
        {
            var release = await _appUpdater.CheckReleaseAsync(
                DisplayedVersion, suffix, LinuxInstall.Value.IncludesPreReleases(DisplayedVersion), CancellationToken.None).ConfigureAwait(true);
            _logger.Info(release is null
                ? $"Release update check: {DisplayedVersion} is up to date ({suffix}, {LinuxInstall.Value})"
                : $"Release update check: {release.Version} available ({suffix}, {LinuxInstall.Value})");
            LastAppUpdateCheck = new AppUpdateCheck(DisplayedVersion, release is not null, DateTimeOffset.Now);
            _stateStore.Save(_stateStore.Load() with { AppUpdateCheck = LastAppUpdateCheck });
            return release;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.Warn(ex, "Release update check failed");
            if (IsCheckingAppUpdate)
            {
                StatusMessage = $"Could not check for a Steward update: {ex.Message}";
            }

            return AppUpdate;
        }
    }

    private async Task InstallReleaseUpdateAsync(AddonRelease release)
    {
        var install = LinuxInstall.Value;
        if (install.Kind == LinuxPackageKind.None)
        {
            OpenUri(AppUpdateChangelogUri!);
            return;
        }

        if (IsAnyRowBusy)
        {
            StatusMessage = "Steward can update once the addon updates in progress finish.";
            return;
        }

        var elevated = Environment.IsPrivilegedProcess;
        var packageFile = Path.GetFileName(new Uri(release.Zip ?? "file:///").AbsolutePath);
        var terminal = LinuxPackages.TerminalCommand(install, packageFile);
        if (!elevated && LinuxPackages.FindOnPath(LinuxPackages.Pkexec, Environment.GetEnvironmentVariable("PATH")) is null)
        {
            _logger.Info("Linux update: pkexec not found, opening the release page");
            ShowTerminalHint($"Steward cannot ask for your password here. To update, run: {terminal}");
            return;
        }

        DirectoryInfo? downloads = null;
        try
        {
            string? packagePath = null;
            if (!install.FromFeed)
            {
                _logger.Info($"Linux update: downloading {release.Version}");
                var total = (ulong)Math.Max(release.Size, 0);
                SetStoreProgress(StoreUpdateProgress.From(AppUpdatePhase.Downloading, 0, 0, total));
                var progress = new Progress<double>(fraction =>
                {
                    if (IsAppUpdateInProgress)
                    {
                        ReportStoreProgress(StoreUpdateProgress.From(AppUpdatePhase.Downloading, fraction * StoreUpdateProgress.DownloadShare, (ulong)(fraction * total), total));
                    }
                });
                // Root installs whatever sits at the path after it was verified, so it lives in a folder only this user can write.
                downloads = Directory.CreateTempSubdirectory("steward-update-");
                packagePath = await _appUpdater.DownloadReleaseAssetAsync(
                    release, LinuxPackages.PackageExtension(install.Kind), downloads.FullName, progress, CancellationToken.None).ConfigureAwait(true);
                FinishStoreProgress("downloading the package", completed: true);
            }
            else
            {
                SetStoreProgress(StoreUpdateProgress.From(AppUpdatePhase.Installing, 1, 0, 0));
            }

            var argv = LinuxPackages.InstallArguments(install, packagePath, elevated);
            _logger.Info($"Linux update: running {string.Join(' ', argv.Take(elevated ? 3 : 4))}");
            var result = await ProcessCommand.RunAsync(argv, CancellationToken.None).ConfigureAwait(true)
                ?? new CommandResult(127, $"{argv[0]} could not be started");
            var outcome = LinuxPackages.Classify(result, elevated);
            _logger.Info($"Linux update: {outcome}, exit code {result.ExitCode}");
            if (outcome != PackageInstallOutcome.Installed)
            {
                _logger.Warn(null, $"Linux update output: {LastLines(result.Output)}");
                FinishStoreProgress("with an error", completed: false);
                switch (outcome)
                {
                    case PackageInstallOutcome.NoAuthenticationAgent:
                        ShowTerminalHint($"Steward cannot ask for your password here. To update, run: {terminal}");
                        break;
                    case PackageInstallOutcome.Dismissed:
                        StatusMessage = "The Steward update was cancelled.";
                        break;
                    case PackageInstallOutcome.NotAuthorized:
                        ShowTerminalHint($"Steward could not get permission to install the update. To update, run: {terminal}");
                        break;
                    default:
                        StatusMessage = $"Could not install the Steward update: {LastLines(result.Output)}";
                        break;
                }

                return;
            }

            if (await InstalledPackageVersionAsync(install.Kind).ConfigureAwait(true) is { } installed && !AppVersion.IsNewer(installed, DisplayedVersion))
            {
                _logger.Info($"Linux update: the package manager left {installed} installed");
                FinishStoreProgress("with nothing newer", completed: false);
                StatusMessage = $"The Steward package feed has no build newer than {DisplayedVersion} yet. Try again in a few minutes.";
                return;
            }

            if (Environment.ProcessPath is { } exe)
            {
                var hasSetsid = LinuxPackages.FindOnPath("setsid", Environment.GetEnvironmentVariable("PATH")) is not null;
                _logger.Info(ProcessCommand.StartDetached(LinuxPackages.RelaunchArguments(Environment.ProcessId, exe, hasSetsid))
                    ? "Linux update: installed, relaunching"
                    : "Linux update: installed, could not start the relauncher");
            }

            QuitRequested?.Invoke();
        }
        catch (UpdateVerificationException ex)
        {
            _logger.Warn(ex, "Linux update refused: could not be verified");
            FinishStoreProgress("with an error", completed: false);
            StatusMessage = $"The Steward update could not be verified: {ex.Message}";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException or UnauthorizedAccessException)
        {
            _logger.Warn(ex, "Linux update install failed");
            FinishStoreProgress("with an error", completed: false);
            StatusMessage = $"Could not install the Steward update: {ex.Message}";
        }
        finally
        {
            downloads?.Delete(recursive: true);
        }
    }

    private (string Target, string Hint)? _updateHint;

    private void ShowTerminalHint(string hint)
    {
        var target = AppUpdateChangelogUri!.OriginalString;
        _updateHint = (target, hint);
        StatusMessage = hint;
        ShellOpen.Open(target);
    }

    private static async Task<string?> InstalledPackageVersionAsync(LinuxPackageKind kind) =>
        LinuxPackages.InstalledVersionArguments(kind) is { } argv
            && await ProcessCommand.RunAsync(argv, CancellationToken.None).ConfigureAwait(true) is { ExitCode: 0 } result
            ? AppVersion.FromPackageVersion(result.Output)
            : null;

    private static string LastLines(string output) =>
        string.Join(" ", output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(3));
}
