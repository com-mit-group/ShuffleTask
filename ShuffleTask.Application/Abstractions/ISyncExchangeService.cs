using ShuffleTask.Application.Models;

namespace ShuffleTask.Application.Abstractions;

public interface ISyncExchangeService
{
    Task<SyncManifest> BuildManifestAsync(SyncPeerContext localPeer, CancellationToken cancellationToken = default);

    Task<SyncTaskRequest> BuildTaskRequestAsync(
        SyncManifest remoteManifest,
        SyncPeerContext localPeer,
        CancellationToken cancellationToken = default);

    Task<SyncTaskBatch> BuildTaskBatchAsync(
        SyncTaskRequest request,
        SyncPeerContext localPeer,
        CancellationToken cancellationToken = default);

    Task<SyncTaskBatch> BuildLocalTaskBatchAsync(
        SyncManifest remoteManifest,
        SyncPeerContext localPeer,
        CancellationToken cancellationToken = default);

    Task<SyncApplyResult> ApplyTaskBatchAsync(
        SyncTaskBatch batch,
        CancellationToken cancellationToken = default);
}
