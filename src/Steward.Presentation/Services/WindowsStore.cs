namespace Steward.App.Services;

public enum StartWithWindowsState
{
    Enabled,
    Disabled,
    DisabledByUser,
    DisabledByPolicy,
    EnabledByPolicy,
}

public sealed record StoreUpdateCheck(int Count, bool CanSilentlyInstall);

public sealed record StoreInstallOutcome(string State, bool Completed, bool Canceled);
