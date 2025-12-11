using Microsoft.Extensions.Logging;
using ShuffleTask.Application.Events;
using Yaref92.Events.Abstractions;

namespace ShuffleTask.Application.Services;

internal class TaskManifestAnnouncedAsyncHandler(ILogger<NetworkSyncService>? logger) : IAsyncEventHandler<TaskManifestAnnounced>
{
    private readonly ILogger<NetworkSyncService>? _logger = logger;

    public Task OnNextAsync(TaskManifestAnnounced domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _logger?.LogDebug("Received task manifest announcement from {DeviceId}", domainEvent.DeviceId);
        return Task.CompletedTask;
    }
}

internal class TaskManifestRequestAsyncHandler(ILogger<NetworkSyncService>? logger) : IAsyncEventHandler<TaskManifestRequest>
{
    private readonly ILogger<NetworkSyncService>? _logger = logger;

    public Task OnNextAsync(TaskManifestRequest domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _logger?.LogDebug("Received task manifest request from {DeviceId}", domainEvent.DeviceId);
        return Task.CompletedTask;
    }
}

internal class TaskBatchResponseAsyncHandler(ILogger<NetworkSyncService>? logger) : IAsyncEventHandler<TaskBatchResponse>
{
    private readonly ILogger<NetworkSyncService>? _logger = logger;

    public Task OnNextAsync(TaskBatchResponse domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _logger?.LogDebug("Received task batch response from {DeviceId}", domainEvent.DeviceId);
        return Task.CompletedTask;
    }
}
