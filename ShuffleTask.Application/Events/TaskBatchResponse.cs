using ShuffleTask.Domain.Entities;
using Yaref92.Events;

namespace ShuffleTask.Application.Events;

public class TaskBatchResponse(IEnumerable<TaskItem> tasks, string deviceId, string? userId) : DomainEventBase()
{
    public IEnumerable<TaskItem> Tasks { get; set; } = tasks;

    public string DeviceId { get; set; } = deviceId;

    public string? UserId { get; set; } = userId;
}
