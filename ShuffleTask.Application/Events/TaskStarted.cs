using Yaref92.Events;

namespace ShuffleTask.Application.Events;

public class TaskStarted(string deviceId, string? userId, string taskId, int minutes = -1) : DomainEventBase()
{

    public string TaskId { get; set; } = taskId;

    public string DeviceId { get; set; } = deviceId;

    public string? UserId { get; set; } = userId;

    public int Minutes { get; set; } = minutes;
}
