using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using ShuffleTask.Application.Events;
using Yaref92.Events.Abstractions;

namespace ShuffleTask.Application.Services;

public class TaskManifestAnnouncedAsyncHandler(ILogger<NetworkSyncService>? logger, NetworkSyncService syncService) : IAsyncEventHandler<TaskManifestAnnounced>
{
    private readonly ILogger<NetworkSyncService>? _logger = logger;
    private readonly NetworkSyncService _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));

    public Task OnNextAsync(TaskManifestAnnounced domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _logger?.LogDebug("Received task manifest announcement from {DeviceId}", domainEvent.DeviceId);
        return _syncService.HandleManifestAnnouncementAsync(domainEvent, cancellationToken);
    }
}

public class TaskManifestRequestAsyncHandler(ILogger<NetworkSyncService>? logger, NetworkSyncService syncService) : IAsyncEventHandler<TaskManifestRequest>
{
    private readonly ILogger<NetworkSyncService>? _logger = logger;
    private readonly NetworkSyncService _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));

    public Task OnNextAsync(TaskManifestRequest domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _logger?.LogDebug("Received task manifest request from {DeviceId}", domainEvent.DeviceId);
        return _syncService.HandleManifestRequestAsync(domainEvent, cancellationToken);
    }
}

public class TaskBatchResponseAsyncHandler(ILogger<NetworkSyncService>? logger, NetworkSyncService syncService) : IAsyncEventHandler<TaskBatchResponse>
{
    private readonly ILogger<NetworkSyncService>? _logger = logger;
    private readonly NetworkSyncService _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));

    public Task OnNextAsync(TaskBatchResponse domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _logger?.LogDebug("Received task batch response from {DeviceId}", domainEvent.DeviceId);
        return _syncService.HandleTaskBatchResponseAsync(domainEvent, cancellationToken);
    }
}
