using Yaref92.Events;

namespace ShuffleTask.Domain.Events;
public class TaskShuffled : DomainEventBase
{
    public Guid TaskId { get; }

    public TaskShuffled(Guid taskId, DateTime occuredAt = default, Guid eventId = default) : base(occuredAt, eventId)
    {
        TaskId = taskId;
    }
}
