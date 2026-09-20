namespace Steward.Core;

public static class GuildRosterSync
{
    public static bool WriteIfChanged(WowInstall install, SyncPayload payload, AppStateStore stateStore)
    {
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(stateStore);

        var fingerprint = StewardSyncFile.Fingerprint(payload);
        var state = stateStore.Load();
        if (state.GuildRosterSync.TryGetValue(install.FlavourPath, out var last) && last == fingerprint)
        {
            return false;
        }

        try
        {
            StewardSyncFile.Write(install.AddOnsPath, payload);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        state.GuildRosterSync[install.FlavourPath] = fingerprint;
        stateStore.Save(state);
        return true;
    }
}
