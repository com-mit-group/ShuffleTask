using Microsoft.Extensions.Logging;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Events;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;
using System;
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
    private readonly AsyncLocal<bool> _suppressBroadcast = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly NetworkedEventAggregator _aggregator;
    private readonly TCPEventTransport _transport;
    private bool _disposed;
    private bool _initialized;

    public NetworkSyncService(
        IStorageService storage,
        AppSettings appSettings,
        NetworkedEventAggregator aggregator,
        IEventTransport transport,
        ILogger<NetworkSyncService>? logger = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _aggregator = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
        _transport = (transport as TCPEventTransport) ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger;
        DeviceId = Environment.MachineName;
        UserId = Environment.UserName;
    }

    public async Task InitAsync(CancellationToken cancellationToken = default)
    {
        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }
            
            // Get NetworkOptions from AppSettings
            var options = NetworkOptions;
            options.Normalize();
            DeviceId = options.DeviceId;
            UserId = options.UserId;

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

    public bool ShouldBroadcast => !_suppressBroadcast.Value;

    private Guid SessionUserGuid => NetworkOptions.ResolveSessionUserId();

    public NetworkOptions NetworkOptions => _appSettings.Network ?? NetworkOptions.CreateDefault();

    public async Task ConnectToPeerAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (_transport is null || string.IsNullOrWhiteSpace(host) || port <= 0)
        {
            return;
        }

        await _transport.ConnectToPeerAsync(SessionUserGuid, host, port, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishTaskUpsertAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var evt = new TaskUpsertedEvent(task, DeviceId, UserId);
        await _aggregator!.PublishEventAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishTaskDeletedAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var evt = new TaskDeletedEvent(taskId, DeviceId, UserId);
        await _aggregator!.PublishEventAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishTaskStartedAsync(string taskId, int minutes = -1, CancellationToken cancellationToken = default)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var evt = new TaskStarted(DeviceId, UserId, taskId, minutes);
        await _aggregator!.PublishEventAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishTimeUpNotificationAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var evt = new TimeUpNotificationEvent(DeviceId, UserId);
        await _aggregator!.PublishEventAsync(evt, cancellationToken).ConfigureAwait(false);
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
        if (_initialized)
        {
            return;
        }
        await InitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InitEventAggregationAsync(CancellationToken cancellationToken)
    {
        RegisterTrackedEventTypes();

        SubscribeToInboundEvents();

        await _transport.StartListeningAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DisposeAsyncCore()
    {
        _aggregator?.Dispose();
        if (_transport is not null)
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void SubscribeToInboundEvents()
    {
        if (_aggregator is null)
        {
            return;
        }

        _aggregator.SubscribeToEventType(new TaskUpsertedAsyncHandler(_logger, _storage));
        _aggregator.SubscribeToEventType(new TaskDeletedAsyncHandler(_logger, _storage));
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
    }
}
