using ShuffleTask.Domain.Entities;
using Yaref92.Events;

namespace ShuffleTask.Application.Events;

public class ShuffleResponseEvent : DomainEventBase
{
    public ShuffleResponseEvent()
    {
    }

    public ShuffleResponseEvent(TaskItem task, string deviceId, string? userId)
    {
        Task = task;
        DeviceId = deviceId;
        UserId = userId;
    }

    public TaskItem? Task { get; set; }

    public DateTimeOffset SelectedAt { get; set; } = DateTimeOffset.UtcNow;

    public string DeviceId { get; set; } = string.Empty;

    public string? UserId { get; set; }
}
