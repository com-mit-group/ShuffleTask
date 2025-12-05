using Microsoft.Extensions.Logging;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Events;
using ShuffleTask.Domain.Entities;
using Yaref92.Events;

namespace ShuffleTask.Application.Services;

public class NetworkSyncService : INetworkSyncService, IDisposable
{
    private readonly NetworkedEventAggregator _aggregator;
    private readonly IStorageService _storage;
    private readonly ILogger<NetworkSyncService>? _logger;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly AsyncLocal<bool> _suppressBroadcast = new();
    private bool _disposed;

    public NetworkSyncService(
        NetworkedEventAggregator aggregator,
        IStorageService storage,
        ILogger<NetworkSyncService>? logger = null)
    {
        _aggregator = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
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
        await _aggregator.PublishAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishTaskDeletedAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        var evt = new TaskDeletedEvent(taskId, DeviceId, UserId);
        await _aggregator.PublishAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishShuffleRequestAsync(string? taskId, CancellationToken cancellationToken = default)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        var evt = new ShuffleRequestEvent(taskId, DeviceId, UserId);
        await _aggregator.PublishAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishShuffleResponseAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        var evt = new ShuffleResponseEvent(task, DeviceId, UserId);
        await _aggregator.PublishAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishNotificationIntentAsync(string title, string message, string? taskId, CancellationToken cancellationToken = default)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        var evt = new NotificationIntentEvent(title, message, DeviceId, UserId, taskId);
        await _aggregator.PublishAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteWithoutBroadcastAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        bool original = _suppressBroadcast.Value;
        _suppressBroadcast.Value = true;
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            _suppressBroadcast.Value = original;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _disposed = true;
    }

    private void SubscribeToInboundEvents()
    {
        _subscriptions.Add(_aggregator.Subscribe<TaskUpsertedEvent>(HandleTaskUpsertAsync));
        _subscriptions.Add(_aggregator.Subscribe<TaskDeletedEvent>(HandleTaskDeletedAsync));
    }

    private async Task HandleTaskUpsertAsync(TaskUpsertedEvent evt)
    {
        if (evt == null || evt.Task == null)
        {
            return;
        }

        if (IsLocalEvent(evt.DeviceId))
        {
            return;
        }

        try
        {
            await ExecuteWithoutBroadcastAsync(async () =>
            {
                var existing = await _storage.GetTaskAsync(evt.Task.Id).ConfigureAwait(false);
                if (existing == null)
                {
                    await _storage.AddTaskAsync(evt.Task).ConfigureAwait(false);
                }
                else
                {
                    await _storage.UpdateTaskAsync(evt.Task).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to apply inbound task upsert for {TaskId}", evt.Task.Id);
        }
    }

    private async Task HandleTaskDeletedAsync(TaskDeletedEvent evt)
    {
        if (evt == null || string.IsNullOrWhiteSpace(evt.TaskId))
        {
            return;
        }

        if (IsLocalEvent(evt.DeviceId))
        {
            return;
        }

        try
        {
            await ExecuteWithoutBroadcastAsync(() => _storage.DeleteTaskAsync(evt.TaskId)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to apply inbound task delete for {TaskId}", evt.TaskId);
        }
    }

    private bool IsLocalEvent(string deviceId)
    {
        return string.Equals(DeviceId, deviceId, StringComparison.OrdinalIgnoreCase);
    }
}
