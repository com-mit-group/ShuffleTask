using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Abstractions;

public interface INetworkSyncService
{
    string DeviceId { get; }

    string? UserId { get; }

    bool ShouldBroadcast { get; }

    Task PublishTaskUpsertAsync(TaskItem task, CancellationToken cancellationToken = default);

    Task PublishTaskDeletedAsync(string taskId, CancellationToken cancellationToken = default);

    Task PublishTaskStartedAsync(string taskId, int minutes = -1, CancellationToken cancellationToken = default);
    Task PublishTimeUpNotificationAsync(CancellationToken cancellationToken = default);
}
