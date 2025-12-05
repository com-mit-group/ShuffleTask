using Yaref92.Events;

namespace ShuffleTask.Application.Events;

public class TaskDeletedEvent : DomainEventBase
{
    public TaskDeletedEvent()
    {
    }

    public TaskDeletedEvent(string taskId, string deviceId, string? userId)
    {
        TaskId = taskId;
        DeviceId = deviceId;
        UserId = userId;
    }

    public string TaskId { get; set; } = string.Empty;

    public string DeviceId { get; set; } = string.Empty;

    public string? UserId { get; set; }
}
