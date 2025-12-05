using Microsoft.Extensions.Logging;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Events;
using ShuffleTask.Domain.Entities;
using System.Threading;
using Yaref92.Events;

namespace ShuffleTask.Application.Services;

public class NetworkSyncService : INetworkSyncService, IDisposable
{
    private readonly NetworkedEventAggregator _aggregator;
    private readonly IStorageService _storage;
    private readonly INotificationService _notifications;
    private readonly ILogger<NetworkSyncService>? _logger;
    private readonly AsyncLocal<bool> _suppressBroadcast = new();
    private bool _disposed;

    public NetworkSyncService(
        NetworkedEventAggregator aggregator,
        IStorageService storage,
        INotificationService notifications,
        ILogger<NetworkSyncService>? logger = null)
    {
        _aggregator = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _notifications = notifications;
        _logger = logger;
        DeviceId = Environment.MachineName;
        UserId = Environment.UserName;

        SubscribeToInboundEvents();
    }

    public string DeviceId { get; }

    public string? UserId { get; }

    public bool ShouldBroadcast => !_suppressBroadcast.Value;

    public async Task PublishTaskUpsertAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        var evt = new TaskUpsertedEvent(task, DeviceId, UserId);
        await _aggregator.PublishEventAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishTaskDeletedAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        var evt = new TaskDeletedEvent(taskId, DeviceId, UserId);
        await _aggregator.PublishEventAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishTaskStartedAsync(string taskId, int minutes = -1, CancellationToken cancellationToken = default)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        var evt = new TaskStarted(DeviceId, UserId, taskId, minutes);
        await _aggregator.PublishEventAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishTimeUpNotificationAsync(CancellationToken cancellationToken = default)
    {
        var evt = new TimeUpNotificationEvent(DeviceId, UserId);
        await _aggregator.PublishEventAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _aggregator.Dispose();

        _disposed = true;
    }

    private void SubscribeToInboundEvents()
    {
        _aggregator.SubscribeToEventType(new TaskUpsertedAsyncHandler(_logger, _storage));
        _aggregator.SubscribeToEventType(new TaskDeletedAsyncHandler(_logger, _storage));
    }
}
