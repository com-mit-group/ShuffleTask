using Microsoft.Extensions.Logging;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Events;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Yaref92.Events;
using Yaref92.Events.Abstractions;
using Yaref92.Events.Serialization;
using Yaref92.Events.Transports;

namespace ShuffleTask.Application.Services;

public class NetworkSyncService : INetworkSyncService, IDisposable
{
    private readonly IStorageService _storage;
    private readonly AppSettings _appSettings;
    private readonly ILogger<NetworkSyncService>? _logger;
    private readonly INotificationService? _notifications;
    private readonly PeerSyncCoordinator _coordinator;
    private readonly AsyncLocal<bool> _suppressBroadcast = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly NetworkedEventAggregator _aggregator;
    private readonly TCPEventTransport _transport;
    private readonly object _connectionLock = new();
    private readonly object _publishLock = new();
    private CancellationTokenSource _peerConnectionCts = new();
    private List<Task> _inFlightPublishes = new();
    private bool _disposed;
    private bool _initialized;

    private const int TaskBatchSize = 10;
    private const string PeerConnect = "Peer connect";

    public NetworkSyncService(
        IStorageService storage,
        AppSettings appSettings,
        NetworkedEventAggregator aggregator,
        IEventTransport transport,
        ILogger<NetworkSyncService>? logger = null,
        INotificationService? notifications = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _aggregator = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
        _transport = (transport as TCPEventTransport) ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger;
        _notifications = notifications;
        _coordinator = new PeerSyncCoordinator(_storage);
        DeviceId = Environment.MachineName;
        UserId = Environment.UserName;
    }

    public async Task InitAsync(CancellationToken cancellationToken = default)
    {
        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RefreshCachedIdentity();

            if (_initialized)
            {
                return;
            }

            await InitEventAggregationAsync(cancellationToken).ConfigureAwait(false);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public string DeviceId { get; private set; }

    public string? UserId { get; private set; }

    public bool ShouldBroadcast => !_suppressBroadcast.Value && !IsAnonymous;

    private Guid SessionUserGuid => NetworkOptions.ResolveSessionUserId();

    public NetworkOptions NetworkOptions => _appSettings.Network ?? NetworkOptions.CreateDefault();

    private bool IsAnonymous => string.IsNullOrWhiteSpace(UserId);//NetworkOptions.AnonymousSession || string.IsNullOrWhiteSpace(UserId);

    public async Task RequestGracefulFlushAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        Task[] pendingPublishes;
        lock (_publishLock)
        {
            pendingPublishes = _inFlightPublishes.ToArray();
        }

        if (pendingPublishes.Length == 0)
        {
            return;
        }

        await Task.WhenAll(pendingPublishes).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ConnectToPeerAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (_transport is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            await DebugToastAsync(PeerConnect, "Peer host is empty; cannot connect.").ConfigureAwait(false);
            return;
        }

        if (port <= 0)
        {
            await DebugToastAsync(PeerConnect, "Peer port is invalid; cannot connect.").ConfigureAwait(false);
            return;
        }

        if (IsAnonymous)
        {
            const string loginToSync = "Log in to sync.";
            await DebugToastAsync(PeerConnect, loginToSync).ConfigureAwait(false);
            throw new InvalidOperationException(loginToSync);
        }

        await DebugToastAsync(PeerConnect, $"Connecting to {host}:{port}...").ConfigureAwait(false);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, EnsureConnectionCts().Token);
        try
        {
            await _transport.ConnectToPeerAsync(SessionUserGuid, host, port, linkedCts.Token).ConfigureAwait(false);

            await PublishManifestAnnouncementAsync(linkedCts.Token).ConfigureAwait(false);

            await DebugToastAsync(PeerConnect, $"Connected to {host}:{port}.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error connecting to peer {Host}:{Port}.", host, port);
            await DebugToastAsync(PeerConnect, $"Failed to connect to {host}:{port}.").ConfigureAwait(false);
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await DebugToastAsync("Peer disconnect", "Disconnecting from peers...").ConfigureAwait(false);
            CancelConnections();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error disconnecting from peers.");
            await DebugToastAsync("Peer disconnect", "Error while disconnecting from peers.").ConfigureAwait(false);
        }
    }

    public async Task PublishTaskUpsertAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (!ShouldBroadcast)
        {
            return;
        }

        var evt = new TaskUpsertedEvent(task, DeviceId, UserId);
        await PublishWithTrackingAsync(() => _aggregator!.PublishEventAsync(evt, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishTaskDeletedAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (!ShouldBroadcast)
        {
            return;
        }

        var evt = new TaskDeletedEvent(taskId, DeviceId, UserId);
        await PublishWithTrackingAsync(() => _aggregator!.PublishEventAsync(evt, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishTaskStartedAsync(string taskId, int minutes = -1, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (!ShouldBroadcast)
        {
            return;
        }

        var evt = new TaskStarted(DeviceId, UserId, taskId, minutes);
        await PublishWithTrackingAsync(() => _aggregator!.PublishEventAsync(evt, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishTimeUpNotificationAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (!ShouldBroadcast)
        {
            return;
        }

        var evt = new TimeUpNotificationEvent(DeviceId, UserId);
        await PublishWithTrackingAsync(() => _aggregator!.PublishEventAsync(evt, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishSettingsUpdatedAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (!ShouldBroadcast)
        {
            return;
        }

        var payload = new AppSettings();
        payload.CopyFrom(settings);
        var evt = new SettingsUpdatedEvent(payload, DeviceId, UserId);
        await PublishWithTrackingAsync(() => _aggregator!.PublishEventAsync(evt, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisposeAsyncCore().GetAwaiter().GetResult();
        _disposed = true;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        await InitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void RefreshCachedIdentity()
    {
        var options = NetworkOptions;
        options.Normalize();
        DeviceId = options.DeviceId;
        UserId = options.UserId;
    }

    private async Task InitEventAggregationAsync(CancellationToken cancellationToken)
    {
        RegisterTrackedEventTypes();

        SubscribeToInboundEvents();

        await _transport.StartListeningAsync(cancellationToken).ConfigureAwait(false);
        await DebugToastAsync("Transport", $"Listening on port {NetworkOptions.ListeningPort}.").ConfigureAwait(false);
    }

    private async Task DisposeAsyncCore()
    {
        _aggregator?.Dispose();
        if (_transport is not null)
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
        }
        CancelConnections();
    }

    private void SubscribeToInboundEvents()
    {
        if (_aggregator is null)
        {
            return;
        }

        _aggregator.SubscribeToEventType(new TaskUpsertedAsyncHandler(_logger, _storage));
        _aggregator.SubscribeToEventType(new TaskDeletedAsyncHandler(_logger, _storage));
        _aggregator.SubscribeToEventType(new TaskManifestAnnouncedAsyncHandler(_logger, this));
        _aggregator.SubscribeToEventType(new TaskManifestRequestAsyncHandler(_logger, this));
        _aggregator.SubscribeToEventType(new TaskBatchResponseAsyncHandler(_logger, this));
        _aggregator.SubscribeToEventType(new SettingsUpdatedAsyncHandler(_logger, _storage, _appSettings));
    }

    private void RegisterTrackedEventTypes()
    {
        if (_aggregator is null)
        {
            return;
        }

        _aggregator.RegisterEventType<TaskUpsertedEvent>();
        _aggregator.RegisterEventType<TaskDeletedEvent>();
        _aggregator.RegisterEventType<TaskStarted>();
        _aggregator.RegisterEventType<TimeUpNotificationEvent>();
        _aggregator.RegisterEventType<TaskManifestAnnounced>();
        _aggregator.RegisterEventType<TaskManifestRequest>();
        _aggregator.RegisterEventType<TaskBatchResponse>();
        _aggregator.RegisterEventType<SettingsUpdatedEvent>();
    }

    private Task DebugToastAsync(string title, string message)
    {
#if DEBUG
        if (_notifications is null)
        {
            return Task.CompletedTask;
        }

        return _notifications.ShowToastAsync(title, message, _appSettings);
#else
        return Task.CompletedTask;
#endif
    }

    private CancellationTokenSource EnsureConnectionCts()
    {
        lock (_connectionLock)
        {
            if (_peerConnectionCts.IsCancellationRequested)
            {
                _peerConnectionCts.Dispose();
                _peerConnectionCts = new CancellationTokenSource();
            }

            return _peerConnectionCts;
        }
    }

    private void CancelConnections()
    {
        lock (_connectionLock)
        {
            var existingCts = _peerConnectionCts;
            _peerConnectionCts = new CancellationTokenSource();

            if (!existingCts.IsCancellationRequested)
            {
                existingCts.Cancel();
            }

            existingCts.Dispose();
        }
    }

    private async Task PublishWithTrackingAsync(Func<Task> publishAction, CancellationToken cancellationToken)
    {
        Task publishTask;
        lock (_publishLock)
        {
            publishTask = publishAction();
            _inFlightPublishes.Add(publishTask);
        }

        try
        {
            await publishTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_publishLock)
            {
                _inFlightPublishes.Remove(publishTask);
            }
        }
    }

    internal async Task HandleManifestAnnouncementAsync(TaskManifestAnnounced domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (domainEvent.Manifest is null)
        {
            return;
        }

        await DebugToastAsync("Manifest announced", $"Received manifest announcement from {domainEvent.DeviceId}.").ConfigureAwait(false);

        var manifest = domainEvent.Manifest.ToList();
        using var linkedCts = LinkToConnection(cancellationToken);

        var comparison = await _coordinator
            .CompareManifestAsync(manifest, UserId, DeviceId)
            .WaitAsync(linkedCts.Token)
            .ConfigureAwait(false);

        var tasksToRequest = comparison.GetTasksToRequest();
        if (tasksToRequest.Count > 0)
        {
            await PublishManifestRequestAsync(manifest, tasksToRequest, linkedCts.Token).ConfigureAwait(false);
        }

        var tasksToAdvertise = comparison.GetTasksToAdvertise();
        if (tasksToAdvertise.Count > 0)
        {
            await PublishTaskBatchesAsync(tasksToAdvertise, linkedCts.Token).ConfigureAwait(false);
        }
    }

    internal async Task HandleManifestRequestAsync(TaskManifestRequest domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (domainEvent.Manifest is null)
        {
            return;
        }

        await DebugToastAsync("Manifest request", $"Received manifest request from {domainEvent.DeviceId}.").ConfigureAwait(false);

        using var linkedCts = LinkToConnection(cancellationToken);

        var requestedTaskIds = domainEvent.Manifest
            .Select(entry => entry.TaskId)
            .ToHashSet(StringComparer.Ordinal);

        string[] tasksToAdvertise = (await _coordinator
            .GetTasksToAdvertiseAsync(domainEvent.Manifest, UserId, DeviceId)
            .WaitAsync(linkedCts.Token)
            .ConfigureAwait(false))
            .Where(requestedTaskIds.Contains)
            .ToArray();

        if (tasksToAdvertise.Length == 0)
        {
            return;
        }

        await PublishTaskBatchesAsync(tasksToAdvertise, linkedCts.Token).ConfigureAwait(false);
    }

    internal async Task HandleTaskBatchResponseAsync(TaskBatchResponse domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (domainEvent.Tasks is null)
        {
            return;
        }

        await DebugToastAsync("Task batch response", $"Received task batch response from {domainEvent.DeviceId}.").ConfigureAwait(false);

        using var linkedCts = LinkToConnection(cancellationToken);
        await RunWithoutBroadcastAsync(async () =>
        {
            foreach (var task in domainEvent.Tasks)
            {
                var upsertedEvent = new TaskUpsertedEvent(task, domainEvent.DeviceId, domainEvent.UserId);
                await _aggregator.PublishEventAsync(upsertedEvent, linkedCts.Token).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    private async Task PublishManifestAnnouncementAsync(CancellationToken cancellationToken)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        var manifest = await BuildLocalManifestAsync(cancellationToken).ConfigureAwait(false);
        var announcement = new TaskManifestAnnounced(manifest, DeviceId, UserId);

        await PublishWithTrackingAsync(() => _aggregator.PublishEventAsync(announcement, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task PublishManifestRequestAsync(
        IReadOnlyCollection<TaskManifestEntry> remoteManifest,
        IReadOnlyCollection<string> tasksToRequest,
        CancellationToken cancellationToken)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        var requestEntries = remoteManifest
            .Where(entry => tasksToRequest.Contains(entry.TaskId))
            .ToArray();

        if (requestEntries.Length == 0)
        {
            return;
        }

        var request = new TaskManifestRequest(requestEntries, DeviceId, UserId);
        await PublishWithTrackingAsync(() => _aggregator.PublishEventAsync(request, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task PublishTaskBatchesAsync(IEnumerable<string> taskIds, CancellationToken cancellationToken)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        foreach (var batch in taskIds.Chunk(TaskBatchSize))
        {
            var tasks = await LoadTasksAsync(batch, cancellationToken).ConfigureAwait(false);
            foreach (var task in tasks)
            {
                await PublishTaskUpsertAsync(task, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<IReadOnlyCollection<TaskManifestEntry>> BuildLocalManifestAsync(CancellationToken cancellationToken)
    {
        var tasks = await _storage.GetTasksAsync(UserId, DeviceId).WaitAsync(cancellationToken).ConfigureAwait(false);
        return tasks.Select(task => new TaskManifestEntry
        {
            TaskId = task.Id,
            EventVersion = task.EventVersion,
            UpdatedAt = task.UpdatedAt,
            DeviceId = task.DeviceId,
            UserId = task.UserId,
        }).ToArray();
    }

    private async Task<IReadOnlyCollection<TaskItem>> LoadTasksAsync(IEnumerable<string> taskIds, CancellationToken cancellationToken)
    {
        var tasks = new List<TaskItem>();

        foreach (var taskId in taskIds)
        {
            if (string.IsNullOrWhiteSpace(taskId))
            {
                continue;
            }

            var task = await _storage.GetTaskAsync(taskId).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (task is not null)
            {
                tasks.Add(task);
            }
        }

        return tasks;
    }

    private async Task RunWithoutBroadcastAsync(Func<Task> action)
    {
        var previous = _suppressBroadcast.Value;
        _suppressBroadcast.Value = true;
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            _suppressBroadcast.Value = previous;
        }
    }

    private CancellationTokenSource LinkToConnection(CancellationToken cancellationToken)
    {
        return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, EnsureConnectionCts().Token);
    }
}
