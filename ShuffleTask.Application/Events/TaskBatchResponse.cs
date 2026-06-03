using ShuffleTask.Domain.Entities;
using ShuffleTask.Application.Models;
using Yaref92.Events;

namespace ShuffleTask.Application.Events;

public class TaskBatchResponse : DomainEventBase
{
    public TaskBatchResponse(IEnumerable<TaskItem> tasks, string deviceId, string? userId)
        : this(new SyncTaskBatch(deviceId, userId, deviceId, tasks), deviceId, userId)
    {
    }

    public TaskBatchResponse(SyncTaskBatch batch, string deviceId, string? userId)
    {
        Batch = batch ?? throw new ArgumentNullException(nameof(batch));
        Tasks = Batch.Tasks;
        DeviceId = deviceId;
        UserId = userId;
    }

    public SyncTaskBatch? Batch { get; set; }

    public IEnumerable<TaskItem> Tasks { get; set; } = Array.Empty<TaskItem>();

    public string DeviceId { get; set; } = string.Empty;

    public string? UserId { get; set; }
}
