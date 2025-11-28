using ShuffleTask.Domain.Events;
using Yaref92.Events.Abstractions;

namespace ShuffleTask.Application.Sync;

public sealed class NotificationBroadcastedEventSubscriber(IEventAggregator eventAggregator) : IAsyncEventSubscriber<NotificationBroadcasted>
{
    private readonly IEventAggregator _eventAggregator = eventAggregator;

    public async Task OnNextAsync(NotificationBroadcasted domainEvent, CancellationToken cancellationToken = default)
    {
        await _eventAggregator.PublishEventAsync(domainEvent, cancellationToken);
    }
}
