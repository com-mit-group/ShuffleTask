using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Models;

public sealed class SyncManifest
{
    public SyncManifest(
        string peerId,
        string? userId,
        string deviceId,
        int schemaVersion,
        IEnumerable<SyncManifestEntry> entries)
    {
        PeerId = SyncIdentity.Required(peerId);
        UserId = SyncIdentity.Optional(userId);
        DeviceId = SyncIdentity.Required(deviceId);
        SchemaVersion = schemaVersion;
        Entries = entries?.ToArray() ?? Array.Empty<SyncManifestEntry>();
    }

    public string PeerId { get; }

    public string? UserId { get; }

    public string DeviceId { get; }

    public int SchemaVersion { get; }

    public IReadOnlyCollection<SyncManifestEntry> Entries { get; }
}

public sealed class SyncManifestEntry
{
    public SyncManifestEntry()
    {
    }

    public SyncManifestEntry(string taskId, int eventVersion, DateTime updatedAtUtc, bool deleted = false)
    {
        TaskId = taskId;
        EventVersion = eventVersion;
        UpdatedAtUtc = updatedAtUtc;
        Deleted = deleted;
    }

    public string TaskId { get; set; } = string.Empty;

    public int EventVersion { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public bool Deleted { get; set; }

    public static SyncManifestEntry From(TaskItem task)
        => new(task.Id, task.EventVersion, EnsureUtc(task.UpdatedAt));

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
