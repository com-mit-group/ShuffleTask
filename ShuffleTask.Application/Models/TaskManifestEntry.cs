namespace ShuffleTask.Application.Models;

public class TaskManifestEntry
{
    public TaskManifestEntry()
    {
    }

    public TaskManifestEntry(string taskId, int eventVersion, DateTime updatedAt)
    {
        TaskId = taskId;
        EventVersion = eventVersion;
        UpdatedAt = updatedAt;
    }

    public string TaskId { get; set; } = string.Empty;

    public int EventVersion { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? DeviceId { get; set; }

    public string? UserId { get; set; }

    public static TaskManifestEntry From(ShuffleTask.Domain.Entities.TaskItem task)
    {
        return new TaskManifestEntry
        {
            TaskId = task.Id,
            EventVersion = task.EventVersion,
            UpdatedAt = task.UpdatedAt,
            DeviceId = task.DeviceId,
            UserId = task.UserId,
        };
    }
}
