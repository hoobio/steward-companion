using Steward.Core.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Steward.Core;

public static class GuildRosterSync
{
    public static bool WriteIfChanged(WowInstall install, SyncPayload payload, AppStateStore stateStore, bool force = false, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(stateStore);
        logger ??= NullLogger.Instance;

        var fingerprint = StewardSyncFile.Fingerprint(payload);
        var state = stateStore.Load();
        if (!force
            && StewardSyncFile.ReadFingerprint(install.AddOnsPath) == fingerprint
            && (payload.Avatar is null || File.Exists(StewardSyncFile.AvatarPathFor(install.AddOnsPath))))
        {
            logger.Info($"StewardSync.lua skipped, unchanged for {install.FlavourPath}");
            return false;
        }

        try
        {
            StewardSyncFile.Write(install.AddOnsPath, payload);
        }
        catch (InvalidOperationException ex)
        {
            logger.Warn(ex, $"StewardSync.lua write failed for {install.FlavourPath}");
            return false;
        }

        state.GuildRosterSync[install.FlavourPath] = fingerprint;
        stateStore.Save(state);
        logger.Info($"StewardSync.lua written for {install.FlavourPath}");
        return true;
    }
}
