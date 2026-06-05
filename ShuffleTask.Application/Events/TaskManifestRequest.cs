using ShuffleTask.Application.Models;
using Yaref92.Events;

namespace ShuffleTask.Application.Events;

public class TaskManifestRequest : DomainEventBase
{
    public TaskManifestRequest(IEnumerable<string> requestedTaskIds, string deviceId, string? userId)
    {
        RequestedTaskIds = SyncIdentity.DistinctIds(requestedTaskIds);
        DeviceId = deviceId;
        UserId = userId;
    }

    public TaskManifestRequest(IEnumerable<TaskManifestEntry> manifest, string deviceId, string? userId)
        : this(manifest?.Select(entry => entry.TaskId) ?? Array.Empty<string>(), deviceId, userId)
    {
        Manifest = manifest ?? Array.Empty<TaskManifestEntry>();
    }

    public IEnumerable<string> RequestedTaskIds { get; set; } = Array.Empty<string>();

    public IEnumerable<TaskManifestEntry>? Manifest { get; set; }

    public string DeviceId { get; set; } = string.Empty;

    public string? UserId { get; set; }
}
