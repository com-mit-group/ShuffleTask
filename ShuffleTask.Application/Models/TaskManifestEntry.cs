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
}
