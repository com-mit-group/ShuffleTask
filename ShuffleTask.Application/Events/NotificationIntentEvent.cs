using Yaref92.Events;

namespace ShuffleTask.Application.Events;

public class NotificationIntentEvent : DomainEventBase
{
    public NotificationIntentEvent()
    {
    }

    public NotificationIntentEvent(string title, string message, string deviceId, string? userId, string? taskId = null)
    {
        Title = title;
        Message = message;
        DeviceId = deviceId;
        UserId = userId;
        TaskId = taskId;
    }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? TaskId { get; set; }

    public DateTimeOffset TriggeredAt { get; set; } = DateTimeOffset.UtcNow;

    public string DeviceId { get; set; } = string.Empty;

    public string? UserId { get; set; }

    public string Intent => string.IsNullOrWhiteSpace(TaskId) ? "system" : "task";
}
