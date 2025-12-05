using Yaref92.Events;

namespace ShuffleTask.Application.Events;

public class TaskDeletedEvent : DomainEventBase
{

    public TaskDeletedEvent(string taskId, string deviceId, string? userId) : base()
    {
        TaskId = taskId;
        DeviceId = deviceId;
        UserId = userId;
    }

    public string TaskId { get; set; }

    public string DeviceId { get; set; }

    public string? UserId { get; set; }
}
