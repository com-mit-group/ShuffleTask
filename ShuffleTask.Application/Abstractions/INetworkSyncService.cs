using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Abstractions;

public interface INetworkSyncService
{
    string DeviceId { get; }

    string? UserId { get; }

    bool ShouldBroadcast { get; }

    Task PublishTaskUpsertAsync(TaskItem task, CancellationToken cancellationToken = default);

    Task PublishTaskDeletedAsync(string taskId, CancellationToken cancellationToken = default);

    Task PublishShuffleRequestAsync(string? taskId, CancellationToken cancellationToken = default);

    Task PublishShuffleResponseAsync(TaskItem task, CancellationToken cancellationToken = default);

    Task PublishNotificationIntentAsync(string title, string message, string? taskId, CancellationToken cancellationToken = default);

    Task ExecuteWithoutBroadcastAsync(Func<Task> action, CancellationToken cancellationToken = default);
}
