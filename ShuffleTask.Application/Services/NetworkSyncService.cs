using Microsoft.Extensions.Logging;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Events;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;
using System;
using System.Threading;
using Yaref92.Events;
using Yaref92.Events.Serialization;
using Yaref92.Events.Transports;

namespace ShuffleTask.Application.Services;

public class NetworkSyncService : INetworkSyncService, IDisposable
{
    private readonly IStorageService _storage;
    private readonly ILogger<NetworkSyncService>? _logger;
    private readonly AsyncLocal<bool> _suppressBroadcast = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private NetworkOptions? _options;
    private NetworkedEventAggregator? _aggregator;
    private TCPEventTransport? _transport;
    private bool _disposed;

    public NetworkSyncService(
        IStorageService storage,
        ILogger<NetworkSyncService>? logger = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger;
        DeviceId = Environment.MachineName;
        UserId = Environment.UserName;
    }

    public string DeviceId { get; private set; }

    public string? UserId { get; private set; }

    public bool ShouldBroadcast => !_suppressBroadcast.Value;

    private Guid SessionUserGuid => (_options ?? NetworkOptions.CreateDefault()).ResolveSessionUserId();

    public NetworkOptions GetCurrentOptions()
    {
        return (_options ?? NetworkOptions.CreateDefault()).Clone();
    }

    public async Task ApplyOptionsAsync(NetworkOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        await EnsureInitializedAsync(options.Clone(), cancellationToken).ConfigureAwait(false);
    }

    public async Task ConnectToPeerAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(null, cancellationToken).ConfigureAwait(false);
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

        await EnsureInitializedAsync(null, cancellationToken).ConfigureAwait(false);
        var evt = new TaskUpsertedEvent(task, DeviceId, UserId);
        await _aggregator!.PublishEventAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishTaskDeletedAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        await EnsureInitializedAsync(null, cancellationToken).ConfigureAwait(false);
        var evt = new TaskDeletedEvent(taskId, DeviceId, UserId);
        await _aggregator!.PublishEventAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishTaskStartedAsync(string taskId, int minutes = -1, CancellationToken cancellationToken = default)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        await EnsureInitializedAsync(null, cancellationToken).ConfigureAwait(false);
        var evt = new TaskStarted(DeviceId, UserId, taskId, minutes);
        await _aggregator!.PublishEventAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishTimeUpNotificationAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(null, cancellationToken).ConfigureAwait(false);
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

    public NetworkedEventAggregator GetAggregator()
    {
        EnsureInitializedAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        return _aggregator ?? throw new InvalidOperationException("Network aggregator not initialized.");
    }

    private async Task EnsureInitializedAsync(NetworkOptions? overrideOptions, CancellationToken cancellationToken)
    {
        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_aggregator is not null && overrideOptions is null)
            {
                return;
            }

            NetworkOptions options = overrideOptions ?? await LoadOptionsAsync(cancellationToken).ConfigureAwait(false);
            options.Normalize();
            DeviceId = options.DeviceId;
            UserId = options.UserId;
            _options = options;

            if (_aggregator is null || _transport is null)
            {
                await BuildTransportAsync(options, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<NetworkOptions> LoadOptionsAsync(CancellationToken cancellationToken)
    {
        await _storage.InitializeAsync().ConfigureAwait(false);
        var settings = await _storage.GetSettingsAsync().ConfigureAwait(false);
        settings.Network ??= NetworkOptions.CreateDefault();
        settings.Network.Normalize();
        return settings.Network.Clone();
    }

    private async Task BuildTransportAsync(NetworkOptions options, CancellationToken cancellationToken)
    {
        options.EnsureListeningPort();
        string authSecret = options.ResolveAuthenticationSecret();

        var localAggregator = new EventAggregator();
        _transport = new TCPEventTransport(
            options.ListeningPort,
            new JsonEventSerializer(),
            TimeSpan.FromSeconds(20),
            authSecret);
        _aggregator = new NetworkedEventAggregator(localAggregator, _transport, ownsLocalAggregator: true, ownsTransport: false);

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
