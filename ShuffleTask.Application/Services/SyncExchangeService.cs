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

        var comparisonManifest = remoteManifest.Entries
            .Where(entry => !entry.Deleted)
            .Select(entry => new TaskManifestEntry(entry.TaskId, entry.EventVersion, entry.UpdatedAtUtc)
            {
                UserId = remoteManifest.UserId,
                DeviceId = remoteManifest.DeviceId,
            })
            .ToArray();

        var comparison = await _coordinator
            .CompareManifestAsync(comparisonManifest, localPeer.UserId, localPeer.DeviceId)
            .WaitAsync(cancellationToken)
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

        var comparisonManifest = remoteManifest.Entries
            .Where(entry => !entry.Deleted)
            .Select(entry => new TaskManifestEntry(entry.TaskId, entry.EventVersion, entry.UpdatedAtUtc)
            {
                UserId = remoteManifest.UserId,
                DeviceId = remoteManifest.DeviceId,
            })
            .ToArray();

        var comparison = await _coordinator
            .CompareManifestAsync(comparisonManifest, localPeer.UserId, localPeer.DeviceId)
            .WaitAsync(cancellationToken)
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
            var incoming = NormalizeIncoming(task, existing);

            if (existing is null)
            {
                await _storage.AddTaskAsync(incoming).WaitAsync(cancellationToken).ConfigureAwait(false);
                applied.Add(incoming.Id);
                continue;
            }

            if (IsStale(incoming, existing))
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

    private static bool MatchesBatchScope(TaskItem task, SyncTaskBatch batch)
    {
        if (!string.IsNullOrWhiteSpace(batch.UserId))
        {
            return string.Equals(task.UserId, batch.UserId, StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(task.UserId);
    }

    private static bool IsStale(TaskItem incoming, TaskItem existing)
    {
        if (incoming.EventVersion > existing.EventVersion)
        {
            return false;
        }

        if (incoming.EventVersion < existing.EventVersion)
        {
            return true;
        }

        if (incoming.UpdatedAt != default && existing.UpdatedAt != default)
        {
            return incoming.UpdatedAt <= existing.UpdatedAt;
        }

        return true;
    }

    private static TaskItem NormalizeIncoming(TaskItem task, TaskItem? existing)
    {
        var normalized = task.Clone();

        if (string.IsNullOrWhiteSpace(normalized.Id))
        {
            normalized.Id = existing?.Id ?? Guid.NewGuid().ToString("n");
        }

        if (normalized.CreatedAt == default)
        {
            normalized.CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow;
        }

        if (normalized.UpdatedAt == default)
        {
            normalized.UpdatedAt = existing?.UpdatedAt ?? DateTime.UtcNow;
        }

        if (normalized.EventVersion <= 0)
        {
            normalized.EventVersion = (existing?.EventVersion ?? 0) + 1;
        }

        if (!string.IsNullOrWhiteSpace(normalized.UserId))
        {
            normalized.DeviceId = null;
        }
        else
        {
            normalized.UserId = existing?.UserId;
            normalized.DeviceId = string.IsNullOrWhiteSpace(normalized.DeviceId)
                ? existing?.DeviceId ?? Environment.MachineName
                : normalized.DeviceId.Trim();
        }

        return normalized;
    }
}
