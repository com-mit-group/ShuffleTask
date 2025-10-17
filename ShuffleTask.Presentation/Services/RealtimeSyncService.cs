using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Sync;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Domain.Events;
using ShuffleTask.Persistence;
using Yaref92.Events;
using Yaref92.Events.Abstractions;
using Yaref92.Events.Serialization;
using Yaref92.Events.Transports;

namespace ShuffleTask.Presentation.Services;

public sealed class RealtimeSyncService : IRealtimeSyncService, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly TimeProvider _clock;
    private readonly Func<StorageService> _storageFactory;
    private readonly INotificationService _notificationService;
    private readonly SyncOptions _options;
    private readonly IShuffleLogger? _logger;
    private readonly EventAggregator _localAggregator;
    private readonly NetworkedEventAggregator _networkAggregator;
    private readonly IEventTransport _transport;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly SemaphoreSlim _pendingGate = new(1, 1);
    private readonly List<PendingSyncEvent> _pendingEvents = new();
    private readonly AsyncLocal<int> _suppression = new();
    private readonly string _pendingPath;
    private readonly object _connectionLock = new();

    private CancellationTokenSource? _reconnectCts;
    private Task? _reconnectTask;
    private bool _initialized;
    private bool _isConnected;
    private string _deviceId;

    public RealtimeSyncService(
        TimeProvider clock,
        Func<StorageService> storageFactory,
        INotificationService notificationService,
        SyncOptions? options = null,
        IShuffleLogger? logger = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _options = options ?? SyncOptions.LoadFromEnvironment();
        _logger = logger;

        _localAggregator = new EventAggregator();
        var serializer = new JsonEventSerializer();
        _transport = new TCPEventTransport(_options.ListenPort, serializer);
        _networkAggregator = new NetworkedEventAggregator(_localAggregator, _transport, _options.DeduplicationWindow);

        _pendingPath = Path.Combine(FileSystem.AppDataDirectory, "sync-pending.json");
        _deviceId = EnsureDeviceId();

        RegisterEventTypes();
        SubscribeToEvents();
        AttachToStorage();
    }

    public string DeviceId => _deviceId;

    public bool IsConnected => _isConnected;

    public bool ShouldBroadcastLocalChanges => _suppression.Value == 0;

    public event EventHandler<TasksChangedEventArgs>? TasksChanged;
    public event EventHandler<ShuffleStateChangedEventArgs>? ShuffleStateChanged;
    public event EventHandler<NotificationBroadcastEventArgs>? NotificationReceived;
    public event EventHandler<SyncStatusChangedEventArgs>? StatusChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await LoadPendingEventsAsync().ConfigureAwait(false);

            if (_options.Enabled && _options.Peers.Count > 0)
            {
                await ConnectToPeersAsync(cancellationToken).ConfigureAwait(false);
                StartReconnectLoop();
            }
            else
            {
                SetConnected(false, null);
            }

            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : DomainEventBase
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _networkAggregator.PublishEventAsync(domainEvent, cancellationToken).ConfigureAwait(false);
            await FlushPendingEventsAsync(cancellationToken).ConfigureAwait(false);
            SetConnected(true, null);
        }
        catch (Exception ex)
        {
            _logger?.LogSyncEvent("PublishFailed", domainEvent.GetType().Name, ex);
            await QueueEventAsync(domainEvent).ConfigureAwait(false);
            try
            {
                await _localAggregator.PublishEventAsync(domainEvent, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignore local propagation failures
            }

            SetConnected(false, ex);
        }
    }

    public IDisposable SuppressBroadcast()
    {
        _suppression.Value++;
        return new SuppressionScope(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_reconnectCts != null)
        {
            _reconnectCts.Cancel();
            if (_reconnectTask != null)
            {
                try
                {
                    await _reconnectTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            _reconnectCts.Dispose();
            _reconnectCts = null;
        }

        if (_transport is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (_transport is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void RegisterEventTypes()
    {
        _networkAggregator.RegisterEventType<TaskUpserted>();
        _networkAggregator.RegisterEventType<TaskDeleted>();
        _networkAggregator.RegisterEventType<ShuffleStateChanged>();
        _networkAggregator.RegisterEventType<NotificationBroadcasted>();
    }

    private void SubscribeToEvents()
    {
        _networkAggregator.SubscribeToEventType(new AsyncDelegateSubscriber<TaskUpserted>(HandleTaskUpsertedAsync));
        _networkAggregator.SubscribeToEventType(new AsyncDelegateSubscriber<TaskDeleted>(HandleTaskDeletedAsync));
        _networkAggregator.SubscribeToEventType(new AsyncDelegateSubscriber<ShuffleStateChanged>(HandleShuffleStateChangedAsync));
        _networkAggregator.SubscribeToEventType(new AsyncDelegateSubscriber<NotificationBroadcasted>(HandleNotificationBroadcastedAsync));
    }

    private void AttachToStorage()
    {
        try
        {
            var storage = _storageFactory();
            storage.AttachSyncService(this);
        }
        catch (Exception ex)
        {
            _logger?.LogSyncEvent("AttachStorageFailed", null, ex);
        }
    }

    private async Task ConnectToPeersAsync(CancellationToken cancellationToken)
    {
        foreach (var peer in _options.Peers)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _transport.ConnectToPeerAsync(peer.Host, peer.Port).ConfigureAwait(false);
                SetConnected(true, null);
            }
            catch (Exception ex)
            {
                _logger?.LogSyncEvent("ConnectFailed", $"{peer.Host}:{peer.Port}", ex);
                SetConnected(false, ex);
            }
        }

        if (_isConnected)
        {
            await FlushPendingEventsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void StartReconnectLoop()
    {
        _reconnectCts = new CancellationTokenSource();
        var token = _reconnectCts.Token;
        _reconnectTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                if (!_isConnected && _options.Enabled)
                {
                    try
                    {
                        await ConnectToPeersAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                try
                {
                    await Task.Delay(_options.ReconnectInterval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    private async Task HandleTaskUpsertedAsync(TaskUpserted evt, CancellationToken cancellationToken)
    {
        if (evt == null)
        {
            return;
        }

        bool isLocal = string.Equals(evt.DeviceId, DeviceId, StringComparison.OrdinalIgnoreCase);
        if (isLocal)
        {
            OnTasksChanged(new[] { evt.Task.Id }, false, evt.DeviceId);
            return;
        }

        using (SuppressBroadcast())
        {
            var storage = _storageFactory();
            bool applied = await storage.ApplyRemoteTaskUpsertAsync(evt.Task, evt.UpdatedAt).ConfigureAwait(false);
            if (applied)
            {
                OnTasksChanged(new[] { evt.Task.Id }, true, evt.DeviceId);
            }
        }
    }

    private async Task HandleTaskDeletedAsync(TaskDeleted evt, CancellationToken cancellationToken)
    {
        if (evt == null)
        {
            return;
        }

        bool isLocal = string.Equals(evt.DeviceId, DeviceId, StringComparison.OrdinalIgnoreCase);
        if (isLocal)
        {
            OnTasksChanged(new[] { evt.TaskId }, false, evt.DeviceId);
            return;
        }

        using (SuppressBroadcast())
        {
            var storage = _storageFactory();
            bool applied = await storage.ApplyRemoteDeletionAsync(evt.TaskId, evt.DeletedAt).ConfigureAwait(false);
            if (applied)
            {
                OnTasksChanged(new[] { evt.TaskId }, true, evt.DeviceId);
            }
        }
    }

    private Task HandleShuffleStateChangedAsync(ShuffleStateChanged evt, CancellationToken cancellationToken)
    {
        if (evt == null)
        {
            return Task.CompletedTask;
        }

        bool isLocal = string.Equals(evt.DeviceId, DeviceId, StringComparison.OrdinalIgnoreCase);
        PersistShuffleState(evt);
        ShuffleStateChanged?.Invoke(this, new ShuffleStateChangedEventArgs(evt, !isLocal));
        return Task.CompletedTask;
    }

    private async Task HandleNotificationBroadcastedAsync(NotificationBroadcasted evt, CancellationToken cancellationToken)
    {
        if (evt == null)
        {
            return;
        }

        bool isLocal = string.Equals(evt.DeviceId, DeviceId, StringComparison.OrdinalIgnoreCase);
        NotificationReceived?.Invoke(this, new NotificationBroadcastEventArgs(evt, !isLocal));

        if (isLocal)
        {
            return;
        }

        try
        {
            var storage = _storageFactory();
            await storage.InitializeAsync().ConfigureAwait(false);
            AppSettings settings = await storage.GetSettingsAsync().ConfigureAwait(false);
            if (settings.EnableNotifications)
            {
                await _notificationService.ShowToastAsync(evt.Title, evt.Message, settings).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogSyncEvent("NotificationRelayFailed", evt.NotificationId, ex);
        }
    }

    private void PersistShuffleState(ShuffleStateChanged evt)
    {
        if (evt.HasActiveTask)
        {
            Preferences.Default.Set(PreferenceKeys.CurrentTaskId, evt.TaskId);
            if (evt.TimerDurationSeconds.HasValue)
            {
                Preferences.Default.Set(PreferenceKeys.TimerDurationSeconds, evt.TimerDurationSeconds.Value);
            }

            if (evt.TimerExpiresUtc.HasValue)
            {
                Preferences.Default.Set(PreferenceKeys.TimerExpiresAt, evt.TimerExpiresUtc.Value.ToString("O", CultureInfo.InvariantCulture));
            }

            Preferences.Default.Set(PreferenceKeys.TimerMode, evt.TimerMode ?? (int)TimerMode.LongInterval);

            if (evt.TimerMode == (int)TimerMode.Pomodoro)
            {
                Preferences.Default.Set(PreferenceKeys.PomodoroPhase, evt.PomodoroPhase ?? 0);
                Preferences.Default.Set(PreferenceKeys.PomodoroCycle, Math.Max(1, evt.PomodoroCycleIndex ?? 1));
                Preferences.Default.Set(PreferenceKeys.PomodoroTotal, Math.Max(1, evt.PomodoroCycleCount ?? 1));
                Preferences.Default.Set(PreferenceKeys.PomodoroFocus, Math.Max(1, evt.FocusMinutes ?? 1));
                Preferences.Default.Set(PreferenceKeys.PomodoroBreak, Math.Max(1, evt.BreakMinutes ?? 1));
            }
            else
            {
                Preferences.Default.Remove(PreferenceKeys.PomodoroPhase);
                Preferences.Default.Remove(PreferenceKeys.PomodoroCycle);
                Preferences.Default.Remove(PreferenceKeys.PomodoroTotal);
                Preferences.Default.Remove(PreferenceKeys.PomodoroFocus);
                Preferences.Default.Remove(PreferenceKeys.PomodoroBreak);
            }
        }
        else
        {
            Preferences.Default.Remove(PreferenceKeys.CurrentTaskId);
            Preferences.Default.Remove(PreferenceKeys.TimerDurationSeconds);
            Preferences.Default.Remove(PreferenceKeys.TimerExpiresAt);
            Preferences.Default.Remove(PreferenceKeys.TimerMode);
            Preferences.Default.Remove(PreferenceKeys.PomodoroPhase);
            Preferences.Default.Remove(PreferenceKeys.PomodoroCycle);
            Preferences.Default.Remove(PreferenceKeys.PomodoroTotal);
            Preferences.Default.Remove(PreferenceKeys.PomodoroFocus);
            Preferences.Default.Remove(PreferenceKeys.PomodoroBreak);
        }
    }

    private async Task QueueEventAsync(DomainEventBase domainEvent)
    {
        string typeName = domainEvent.GetType().AssemblyQualifiedName ?? domainEvent.GetType().FullName ?? domainEvent.GetType().Name;
        string payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), SerializerOptions);
        var pending = new PendingSyncEvent(typeName, payload, _clock.GetUtcNow());

        await _pendingGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _pendingEvents.Add(pending);
            await SavePendingEventsAsync().ConfigureAwait(false);
        }
        finally
        {
            _pendingGate.Release();
        }
    }

    private async Task LoadPendingEventsAsync()
    {
        if (!File.Exists(_pendingPath))
        {
            return;
        }

        try
        {
            string json = await File.ReadAllTextAsync(_pendingPath).ConfigureAwait(false);
            var items = JsonSerializer.Deserialize<List<PendingSyncEvent>>(json, SerializerOptions);
            if (items != null)
            {
                _pendingEvents.Clear();
                _pendingEvents.AddRange(items);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogSyncEvent("PendingLoadFailed", null, ex);
            _pendingEvents.Clear();
        }
    }

    private async Task SavePendingEventsAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_pendingPath)!);
            string json = JsonSerializer.Serialize(_pendingEvents, SerializerOptions);
            await File.WriteAllTextAsync(_pendingPath, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogSyncEvent("PendingSaveFailed", null, ex);
        }
    }

    private async Task FlushPendingEventsAsync(CancellationToken cancellationToken)
    {
        await _pendingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_pendingEvents.Count == 0)
            {
                return;
            }

            var remaining = new List<PendingSyncEvent>();
            foreach (var pending in _pendingEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    Type? type = Type.GetType(pending.TypeName, throwOnError: false);
                    if (type == null)
                    {
                        continue;
                    }

                    var domainEvent = (DomainEventBase?)JsonSerializer.Deserialize(pending.Payload, type, SerializerOptions);
                    if (domainEvent == null)
                    {
                        continue;
                    }

                    await _networkAggregator.PublishEventAsync(domainEvent, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogSyncEvent("ReplayFailed", pending.TypeName, ex);
                    remaining.Add(pending);
                }
            }

            _pendingEvents.Clear();
            _pendingEvents.AddRange(remaining);
            await SavePendingEventsAsync().ConfigureAwait(false);
        }
        finally
        {
            _pendingGate.Release();
        }
    }

    private void OnTasksChanged(IEnumerable<string> taskIds, bool remote, string? origin)
    {
        var ids = taskIds?.ToArray() ?? Array.Empty<string>();
        TasksChanged?.Invoke(this, new TasksChangedEventArgs(ids, remote, origin));
    }

    private void ReleaseSuppression()
    {
        if (_suppression.Value > 0)
        {
            _suppression.Value--;
        }
    }

    private void SetConnected(bool connected, Exception? error)
    {
        bool changed;
        lock (_connectionLock)
        {
            changed = _isConnected != connected;
            _isConnected = connected;
        }

        if (changed)
        {
            StatusChanged?.Invoke(this, new SyncStatusChangedEventArgs(connected, error));
        }
    }

    private string EnsureDeviceId()
    {
        string existing = Preferences.Default.Get(PreferenceKeys.DeviceId, string.Empty);
        if (string.IsNullOrWhiteSpace(existing))
        {
            existing = Guid.NewGuid().ToString("N");
            Preferences.Default.Set(PreferenceKeys.DeviceId, existing);
        }

        return existing;
    }

    private sealed record PendingSyncEvent(string TypeName, string Payload, DateTimeOffset QueuedAt);

    private sealed class SuppressionScope : IDisposable
    {
        private readonly RealtimeSyncService _owner;
        private bool _disposed;

        public SuppressionScope(RealtimeSyncService owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _owner.ReleaseSuppression();
            _disposed = true;
        }
    }

    private sealed class AsyncDelegateSubscriber<TEvent> : IAsyncEventSubscriber<TEvent>
        where TEvent : DomainEventBase
    {
        private readonly Func<TEvent, CancellationToken, Task> _handler;

        public AsyncDelegateSubscriber(Func<TEvent, CancellationToken, Task> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public Task OnNextAsync(TEvent @event, CancellationToken cancellationToken = default)
            => _handler(@event, cancellationToken);
    }
}
