using Microsoft.Extensions.Logging;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Events;
using ShuffleTask.Application.Exceptions;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Utilities;
using ShuffleTask.Domain.Entities;
using Yaref92.Events;
using Yaref92.Events.Abstractions;
using Yaref92.Events.Transport.Grpc;

namespace ShuffleTask.Application.Services;

public sealed class NetworkSyncService : INetworkSyncService, IDisposable
{
    private readonly IStorageService _storage;
    private readonly AppSettings _appSettings;
    private readonly ILogger<NetworkSyncService>? _logger;
#if DEBUG
    private readonly INotificationService? _notifications;
#endif
    private readonly ISyncExchangeService _syncExchange;
    private readonly AsyncLocal<bool> _suppressBroadcast = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly NetworkedEventAggregator _aggregator;
    private readonly IEventTransport _transport;
    private readonly object _connectionLock = new();
    private readonly object _publishLock = new();
    private CancellationTokenSource _peerConnectionCts = new();
    private readonly List<Task> _inFlightPublishes = new();
    private bool _disposed;
    private bool _initialized;

    private const int TaskBatchSize = 10;
    private const string PeerConnect = "Peer connect";

    public NetworkSyncService(
        IStorageService storage,
        AppSettings appSettings,
        NetworkedEventAggregator aggregator,
        IEventTransport transport,
        ISyncExchangeService syncExchange,
        ILogger<NetworkSyncService>? logger = null,
        INotificationService? notifications = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _aggregator = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _syncExchange = syncExchange ?? throw new ArgumentNullException(nameof(syncExchange));
        _logger = logger;
#if DEBUG
        _notifications = notifications;
#endif
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

    private bool IsAnonymous => string.IsNullOrWhiteSpace(UserId);

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

    public async Task ConnectToPeerAsync(string host, int port, string selectedPeerPlatform, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (_transport is null)
        {
            return;
        }
        (_transport as GrpcEventTransport)?.TargetPlatform = UtilityMethods.ParsePlatform(selectedPeerPlatform);

        if (!await ValidatePeerConnectionAsync(host, port).ConfigureAwait(false))
        {
            return;
        }

        await ConnectToPeerInternalAsync(host, port, cancellationToken).ConfigureAwait(false);
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

    private async Task<bool> ValidatePeerConnectionAsync(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            await DebugToastAsync(PeerConnect, "Peer host is empty; cannot connect.").ConfigureAwait(false);
            return false;
        }

        if (port <= 0)
        {
            await DebugToastAsync(PeerConnect, "Peer port is invalid; cannot connect.").ConfigureAwait(false);
            return false;
        }

        if (IsAnonymous)
        {
            const string loginToSync = "Log in to sync.";
            await DebugToastAsync(PeerConnect, loginToSync).ConfigureAwait(false);
            throw new InvalidOperationException(loginToSync);
        }

        return true;
    }

    private async Task ConnectToPeerInternalAsync(string host, int port, CancellationToken cancellationToken)
    {
        await DebugToastAsync(PeerConnect, $"Connecting to {host}:{port}...").ConfigureAwait(false);

        var sessionUserId = SessionUserGuid;
        await LogConnectionAttemptAsync(sessionUserId, host, port).ConfigureAwait(false);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, EnsureConnectionCts().Token);
        try
        {
            await _transport.ConnectToPeerAsync(sessionUserId, host, port, linkedCts.Token).ConfigureAwait(false);

            await PublishManifestAnnouncementAsync(linkedCts.Token).ConfigureAwait(false);

            await DebugToastAsync(PeerConnect, $"Connected to {host}:{port}.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error connecting to peer {Host}:{Port}.", host, port);
            string message = $"Failed to connect to {host}:{port}. {ex.Message}";
            await DebugToastAsync(PeerConnect, message).ConfigureAwait(false);
            throw new NetworkConnectionException(message, ex);
        }
    }

    private async Task LogConnectionAttemptAsync(Guid sessionUserId, string host, int port)
    {
        await DebugToastAsync(PeerConnect, $"Using UserId '{UserId}' with SessionUserId '{sessionUserId}'.").ConfigureAwait(false);
        _logger?.LogDebug(
            "Using UserId {UserId} with SessionUserId {SessionUserId} before connecting to {Host}:{Port}.",
            UserId,
            sessionUserId,
            host,
            port);
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

        await (_transport as GrpcEventTransport)!.StartListeningAsync(cancellationToken).ConfigureAwait(false);
        await DebugToastAsync("Transport", $"Listening on port {NetworkOptions.ListeningPort}.").ConfigureAwait(false);
    }

    private async Task DisposeAsyncCore()
    {
        _aggregator?.Dispose();
        if (_transport is not null)
        {
            await (_transport as GrpcEventTransport)!.DisposeAsync().ConfigureAwait(false);
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
        _logger?.LogDebug("{Title}: {Message}", title, message);
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

        using var linkedCts = LinkToConnection(cancellationToken);
        var remoteManifest = BuildRemoteManifest(domainEvent);
        var localPeer = CreateLocalPeerContext();

        var request = await _syncExchange
            .BuildTaskRequestAsync(remoteManifest, localPeer, linkedCts.Token)
            .WaitAsync(linkedCts.Token)
            .ConfigureAwait(false);

        if (request.RequestedTaskIds.Count > 0)
        {
            await PublishManifestRequestAsync(request, linkedCts.Token).ConfigureAwait(false);
        }

        var localBatch = await _syncExchange
            .BuildLocalTaskBatchAsync(remoteManifest, localPeer, linkedCts.Token)
            .WaitAsync(linkedCts.Token)
            .ConfigureAwait(false);

        if (localBatch.Tasks.Count > 0)
        {
            await PublishTaskBatchesAsync(localBatch, linkedCts.Token).ConfigureAwait(false);
        }
    }

    internal async Task HandleManifestRequestAsync(TaskManifestRequest domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (domainEvent.RequestedTaskIds is null)
        {
            return;
        }

        await DebugToastAsync("Manifest request", $"Received manifest request from {domainEvent.DeviceId}.").ConfigureAwait(false);

        using var linkedCts = LinkToConnection(cancellationToken);
        var request = new SyncTaskRequest(
            domainEvent.DeviceId,
            domainEvent.UserId,
            domainEvent.DeviceId,
            domainEvent.RequestedTaskIds);

        var batch = await _syncExchange
            .BuildTaskBatchAsync(request, CreateLocalPeerContext(), linkedCts.Token)
            .WaitAsync(linkedCts.Token)
            .ConfigureAwait(false);

        if (batch.Tasks.Count == 0)
        {
            return;
        }

        await PublishTaskBatchesAsync(batch, linkedCts.Token).ConfigureAwait(false);
    }

    internal async Task HandleTaskBatchResponseAsync(TaskBatchResponse domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var batch = domainEvent.Batch ?? new SyncTaskBatch(
            domainEvent.DeviceId,
            domainEvent.UserId,
            domainEvent.DeviceId,
            domainEvent.Tasks ?? Array.Empty<TaskItem>());

        if (batch.Tasks.Count == 0)
        {
            return;
        }

        await DebugToastAsync("Task batch response", $"Received task batch response from {domainEvent.DeviceId}.").ConfigureAwait(false);

        using var linkedCts = LinkToConnection(cancellationToken);
        await RunWithoutBroadcastAsync(() => _syncExchange.ApplyTaskBatchAsync(batch, linkedCts.Token)).ConfigureAwait(false);
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

    private async Task PublishManifestRequestAsync(SyncTaskRequest request, CancellationToken cancellationToken)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        if (request.RequestedTaskIds.Count == 0)
        {
            return;
        }

        var domainEvent = new TaskManifestRequest(request.RequestedTaskIds, DeviceId, UserId);
        await PublishWithTrackingAsync(() => _aggregator.PublishEventAsync(domainEvent, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task PublishTaskBatchesAsync(SyncTaskBatch batch, CancellationToken cancellationToken)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        foreach (var taskChunk in batch.Tasks.Chunk(TaskBatchSize))
        {
            var chunkBatch = new SyncTaskBatch(DeviceId, batch.UserId, DeviceId, taskChunk, batch.DeletedTaskIds);
            var response = new TaskBatchResponse(chunkBatch, DeviceId, UserId);
            await PublishWithTrackingAsync(() => _aggregator.PublishEventAsync(response, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyCollection<TaskManifestEntry>> BuildLocalManifestAsync(CancellationToken cancellationToken)
    {
        var manifest = await _syncExchange
            .BuildManifestAsync(CreateLocalPeerContext(), cancellationToken)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        return manifest.Entries.Select(entry => new TaskManifestEntry
        {
            TaskId = entry.TaskId,
            EventVersion = entry.EventVersion,
            UpdatedAt = entry.UpdatedAtUtc,
            DeviceId = manifest.DeviceId,
            UserId = manifest.UserId,
        }).ToArray();
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

    private SyncPeerContext CreateLocalPeerContext()
        => new(DeviceId, UserId, DeviceId);

    private static SyncManifest BuildRemoteManifest(TaskManifestAnnounced domainEvent)
    {
        var entries = domainEvent.Manifest.Select(entry => new SyncManifestEntry(
            entry.TaskId,
            entry.EventVersion,
            entry.UpdatedAt));

        return new SyncManifest(
            domainEvent.DeviceId,
            domainEvent.UserId,
            domainEvent.DeviceId,
            schemaVersion: 1,
            entries);
    }
}
