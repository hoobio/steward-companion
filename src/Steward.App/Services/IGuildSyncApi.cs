using Steward.Core;

namespace Steward.App.Services;

public interface IGuildSyncApi
{
    Task<SyncServerState> GetStateAsync(CancellationToken ct);

    Task<SyncPushResult> PushAsync(string dataset, SyncPayload payload, IProgress<double>? progress, CancellationToken ct);

    Task<SyncPayload> PullAsync(CancellationToken ct);
}
