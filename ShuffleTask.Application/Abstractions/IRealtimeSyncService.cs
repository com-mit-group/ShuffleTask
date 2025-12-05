using Yaref92.Events;
using Yaref92.Events.Abstractions;

namespace ShuffleTask.Application.Abstractions;

public interface IRealtimeSyncService
{
    string DeviceId { get; }

    bool IsConnected { get; }

    bool ShouldBroadcastLocalChanges { get; }

    IEventAggregator Aggregator { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default, bool connectPeers = true);

    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : DomainEventBase;

    IDisposable SuppressBroadcast();
}
