using System;
using System.Threading;
using System.Threading.Tasks;
using ShuffleTask.Application.Sync;
using Yaref92.Events;

namespace ShuffleTask.Application.Abstractions;

public interface IRealtimeSyncService
{
    string DeviceId { get; }

    bool IsConnected { get; }

    bool ShouldBroadcastLocalChanges { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : DomainEventBase;

    IDisposable SuppressBroadcast();

    event EventHandler<TasksChangedEventArgs> TasksChanged;

    event EventHandler<ShuffleStateChangedEventArgs> ShuffleStateChanged;

    event EventHandler<NotificationBroadcastEventArgs> NotificationReceived;

    event EventHandler<SyncStatusChangedEventArgs> StatusChanged;
}
