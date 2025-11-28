using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ShuffleTask.Application.Sync;
using ShuffleTask.Domain.Events;
using Yaref92.Events;

namespace ShuffleTask.Tests;

[TestFixture]
public sealed class NotificationBroadcastedEventSubscriberTests
{
    [Test]
    public async Task OnNextAsync_ForwardsEventToAggregator()
    {
        var aggregator = new EventAggregator();
        aggregator.RegisterEventType<NotificationBroadcasted>();
        var subscriber = new NotificationBroadcastedEventSubscriber(aggregator);
        NotificationBroadcasted? received = null;
        aggregator.SubscribeToEventType<NotificationBroadcasted>(new RecordingSubscriber(evt =>
        {
            received = evt;
            return Task.CompletedTask;
        }));

        var domainEvent = new NotificationBroadcasted(
            new NotificationBroadcasted.NotificationIdentity("notif-42", "peer"),
            new NotificationBroadcasted.NotificationContent("Remote", "sync"),
            new NotificationBroadcasted.NotificationSchedule(null, DateTime.UtcNow, null),
            isReminder: false);

        await subscriber.OnNextAsync(domainEvent);

        Assert.That(received, Is.Not.Null, "Aggregator should receive forwarded notification events.");
        Assert.That(received!.NotificationId, Is.EqualTo("notif-42"));
    }

    private sealed class RecordingSubscriber(Func<NotificationBroadcasted, Task> callback) : Yaref92.Events.Abstractions.IAsyncEventSubscriber<NotificationBroadcasted>
    {
        public Task OnNextAsync(NotificationBroadcasted @event, CancellationToken cancellationToken = default)
            => callback(@event);
    }
}
