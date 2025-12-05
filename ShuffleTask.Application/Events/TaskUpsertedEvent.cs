using ShuffleTask.Domain.Entities;
using Yaref92.Events;

namespace ShuffleTask.Application.Events;

public class TaskUpsertedEvent(TaskItem task, string deviceId, string? userId) : DomainEventBase()
{
    public TaskItem? Task { get; set; } = task;

    public string DeviceId { get; set; } = deviceId;

    public string? UserId { get; set; } = userId;
}
