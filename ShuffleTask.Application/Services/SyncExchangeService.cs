using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Services;

public sealed class SyncExchangeService : ISyncExchangeService
{
    private const int CurrentSyncSchemaVersion = 1;

    private readonly IStorageService _storage;
    private readonly PeerSyncCoordinator _coordinator;

    public SyncExchangeService(IStorageService storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _coordinator = new PeerSyncCoordinator(storage);
    }

    public async Task<SyncManifest> BuildManifestAsync(
        SyncPeerContext localPeer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localPeer);

        var tasks = await _storage
            .GetTasksAsync(localPeer.UserId, localPeer.DeviceId)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        var entries = tasks.Select(SyncManifestEntry.From).ToArray();
        return new SyncManifest(
            localPeer.PeerId,
            localPeer.UserId,
            localPeer.DeviceId,
            CurrentSyncSchemaVersion,
            entries);
    }

    public async Task<SyncTaskRequest> BuildTaskRequestAsync(
        SyncManifest remoteManifest,
        SyncPeerContext localPeer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteManifest);
        ArgumentNullException.ThrowIfNull(localPeer);

        var comparison = await CompareRemoteManifestAsync(remoteManifest, localPeer, cancellationToken)
            .ConfigureAwait(false);

        return new SyncTaskRequest(
            localPeer.PeerId,
            localPeer.UserId,
            localPeer.DeviceId,
            comparison.GetTasksToRequest());
    }

    public async Task<SyncTaskBatch> BuildTaskBatchAsync(
        SyncTaskRequest request,
        SyncPeerContext localPeer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(localPeer);

        var tasks = new List<TaskItem>();
        foreach (var taskId in request.RequestedTaskIds)
        {
            var task = await _storage.GetTaskAsync(taskId).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (task is null || !MatchesRequestedScope(task, request))
            {
                continue;
            }

            tasks.Add(task);
        }

        return new SyncTaskBatch(localPeer.PeerId, request.UserId, localPeer.DeviceId, tasks);
    }

    public async Task<SyncTaskBatch> BuildLocalTaskBatchAsync(
        SyncManifest remoteManifest,
        SyncPeerContext localPeer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteManifest);
        ArgumentNullException.ThrowIfNull(localPeer);

        var comparison = await CompareRemoteManifestAsync(remoteManifest, localPeer, cancellationToken)
            .ConfigureAwait(false);

        var request = new SyncTaskRequest(
            localPeer.PeerId,
            localPeer.UserId,
            localPeer.DeviceId,
            comparison.GetTasksToAdvertise());

        return await BuildTaskBatchAsync(request, localPeer, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SyncApplyResult> ApplyTaskBatchAsync(
        SyncTaskBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var applied = new List<string>();
        var ignored = new List<string>();

        foreach (var task in batch.Tasks)
        {
            if (task is null || !MatchesBatchScope(task, batch))
            {
                if (task is not null)
                {
                    ignored.Add(task.Id);
                }

                continue;
            }

            var existing = await _storage.GetTaskAsync(task.Id).WaitAsync(cancellationToken).ConfigureAwait(false);
            var incoming = TaskSyncMerge.NormalizeIncoming(task, existing);

            if (existing is null)
            {
                await _storage.AddTaskAsync(incoming).WaitAsync(cancellationToken).ConfigureAwait(false);
                applied.Add(incoming.Id);
                continue;
            }

            if (TaskSyncMerge.IsStaleBatchTask(incoming, existing))
            {
                ignored.Add(incoming.Id);
                continue;
            }

            incoming.CreatedAt = existing.CreatedAt;
            await _storage.UpdateTaskAsync(incoming).WaitAsync(cancellationToken).ConfigureAwait(false);
            applied.Add(incoming.Id);
        }

        return new SyncApplyResult(applied, ignored);
    }

    private static bool MatchesRequestedScope(TaskItem task, SyncTaskRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            return string.Equals(task.UserId, request.UserId, StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(task.UserId)
            && string.Equals(task.DeviceId, request.DeviceId, StringComparison.Ordinal);
    }

    private async Task<ManifestComparisonResult> CompareRemoteManifestAsync(
        SyncManifest remoteManifest,
        SyncPeerContext localPeer,
        CancellationToken cancellationToken)
    {
        var comparisonManifest = remoteManifest.Entries
            .Where(entry => !entry.Deleted)
            .Select(entry => ToLegacyManifestEntry(entry, remoteManifest))
            .ToArray();

        return await _coordinator
            .CompareManifestAsync(comparisonManifest, localPeer.UserId, localPeer.DeviceId)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static TaskManifestEntry ToLegacyManifestEntry(SyncManifestEntry entry, SyncManifest manifest)
        => new(entry.TaskId, entry.EventVersion, entry.UpdatedAtUtc)
        {
            UserId = manifest.UserId,
            DeviceId = manifest.DeviceId,
        };

    private static bool MatchesBatchScope(TaskItem task, SyncTaskBatch batch)
    {
        if (!string.IsNullOrWhiteSpace(batch.UserId))
        {
            return string.Equals(task.UserId, batch.UserId, StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(task.UserId);
    }

}
