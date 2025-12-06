using ShuffleTask.Domain.Entities;
using ShuffleTask.Application.Models;

namespace ShuffleTask.Application.Abstractions;

public interface INetworkSyncService
{
    string DeviceId { get; }

    string? UserId { get; }

    bool ShouldBroadcast { get; }

    NetworkOptions NetworkOptions { get; }

    Task ConnectToPeerAsync(string host, int port, CancellationToken cancellationToken = default);

    Task PublishTaskUpsertAsync(TaskItem task, CancellationToken cancellationToken = default);

    Task PublishTaskDeletedAsync(string taskId, CancellationToken cancellationToken = default);

    Task PublishTaskStartedAsync(string taskId, int minutes = -1, CancellationToken cancellationToken = default);
    Task PublishTimeUpNotificationAsync(CancellationToken cancellationToken = default);
    Task InitAsync(CancellationToken cancellationToken = default);
}
