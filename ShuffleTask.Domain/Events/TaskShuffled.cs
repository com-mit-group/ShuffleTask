using Yaref92.Events;

namespace ShuffleTask.Domain.Events;
public class TaskShuffled : DomainEventBase
{
    public Guid TaskId { get; }

    public TaskShuffled(Guid taskId, DateTime dateTimeOccurredUtc = default, Guid eventId = default)
        : base(dateTimeOccurredUtc, eventId)
    {
        TaskId = taskId;
    }
}
