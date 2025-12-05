using Microsoft.Extensions.Logging;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Events;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;
using System.Threading;
using Yaref92.Events;
using Yaref92.Events.Abstractions;
using Yaref92.Events.Serialization;
using Yaref92.Events.Transports;

namespace ShuffleTask.Application.Services;

public class NetworkSyncService : INetworkSyncService, IDisposable
{
    private readonly IStorageService _storage;
    private readonly ILogger<NetworkSyncService>? _logger;
    private readonly AsyncLocal<bool> _suppressBroadcast = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly List<Action<NetworkedEventAggregator>> _inboundSubscriptions = new();
    private NetworkOptions? _options;
    private NetworkedEventAggregator? _aggregator;
    private TCPEventTransport? _transport;
    private EventAggregator? _localAggregator;
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
        await InitializeAsync(options.Clone(), cancellationToken).ConfigureAwait(false);
    }

    public async Task RegisterInboundHandlerAsync<T>(IAsyncEventHandler<T> handler) where T : class, IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        await InitializeAsync(null, CancellationToken.None).ConfigureAwait(false);
        _aggregator?.SubscribeToEventType(handler);
        _inboundSubscriptions.Add(agg => agg.SubscribeToEventType(handler));
    }

    public async Task ConnectToPeerAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(null, cancellationToken).ConfigureAwait(false);
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

        await InitializeAsync(null, cancellationToken).ConfigureAwait(false);
        var evt = new TaskUpsertedEvent(task, DeviceId, UserId);
        await _aggregator!.PublishEventAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishTaskDeletedAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        await InitializeAsync(null, cancellationToken).ConfigureAwait(false);
        var evt = new TaskDeletedEvent(taskId, DeviceId, UserId);
        await _aggregator!.PublishEventAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishTaskStartedAsync(string taskId, int minutes = -1, CancellationToken cancellationToken = default)
    {
        if (!ShouldBroadcast)
        {
            return;
        }

        await InitializeAsync(null, cancellationToken).ConfigureAwait(false);
        var evt = new TaskStarted(DeviceId, UserId, taskId, minutes);
        await _aggregator!.PublishEventAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishTimeUpNotificationAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(null, cancellationToken).ConfigureAwait(false);
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

    private async Task InitializeAsync(NetworkOptions? overrideOptions, CancellationToken cancellationToken)
    {
        if (_aggregator is not null && overrideOptions is null)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            NetworkOptions options = overrideOptions ?? await LoadOptionsAsync(cancellationToken).ConfigureAwait(false);
            options.Normalize();
            DeviceId = options.DeviceId;
            UserId = options.UserId;

            bool shouldRebuild = _aggregator is null
                || _transport is null
                || _options is null
                || _options.ListeningPort != options.ListeningPort
                || !string.Equals(_options.Host, options.Host, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(_options.DeviceId, options.DeviceId, StringComparison.Ordinal)
                || !string.Equals(_options.UserId, options.UserId, StringComparison.Ordinal)
                || !string.Equals(_options.ResolveAuthenticationSecret(), options.ResolveAuthenticationSecret(), StringComparison.Ordinal);

            _options = options;

            if (shouldRebuild)
            {
                await RebuildTransportAsync(options, cancellationToken).ConfigureAwait(false);
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

    private async Task RebuildTransportAsync(NetworkOptions options, CancellationToken cancellationToken)
    {
        await DisposeAsyncCore().ConfigureAwait(false);

        options.EnsureListeningPort();
        string authSecret = options.ResolveAuthenticationSecret();

        _localAggregator = new EventAggregator();
        _transport = new TCPEventTransport(
            options.ListeningPort,
            new JsonEventSerializer(),
            TimeSpan.FromSeconds(20),
            authSecret);
        _aggregator = new NetworkedEventAggregator(_localAggregator, _transport, ownsLocalAggregator: true, ownsTransport: false);

        SubscribeToInboundEvents();

        await _transport.StartListeningAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DisposeAsyncCore()
    {
        _aggregator?.Dispose();
        _aggregator = null;
        _localAggregator = null;

        if (_transport is not null)
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
            _transport = null;
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
        foreach (var subscription in _inboundSubscriptions)
        {
            subscription(_aggregator);
        }
    }
}
