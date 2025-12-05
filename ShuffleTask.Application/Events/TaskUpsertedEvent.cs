using ShuffleTask.Domain.Entities;
using Yaref92.Events;

namespace ShuffleTask.Application.Events;

public class TaskUpsertedEvent : DomainEventBase
{
    public TaskUpsertedEvent()
    {
    }

    public TaskUpsertedEvent(TaskItem task, string deviceId, string? userId)
    {
        Task = task;
        DeviceId = deviceId;
        UserId = userId;
    }

    public TaskItem? Task { get; set; }

    public string DeviceId { get; set; } = string.Empty;

    public string? UserId { get; set; }
}
