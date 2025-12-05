using Yaref92.Events;

namespace ShuffleTask.Application.Events;

public class ShuffleRequestEvent : DomainEventBase
{
    public ShuffleRequestEvent()
    {
    }

    public ShuffleRequestEvent(string? taskId, string deviceId, string? userId)
    {
        TaskId = taskId;
        DeviceId = deviceId;
        UserId = userId;
    }

    public string? TaskId { get; set; }

    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;

    public string DeviceId { get; set; } = string.Empty;

    public string? UserId { get; set; }
}
