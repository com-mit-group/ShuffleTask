using ShuffleTask.Application.Models;
using Yaref92.Events;

namespace ShuffleTask.Application.Events;

public class TaskManifestAnnounced(IEnumerable<TaskManifestEntry> manifest, string deviceId, string? userId) : DomainEventBase()
{
    public IEnumerable<TaskManifestEntry> Manifest { get; set; } = manifest;

    public string DeviceId { get; set; } = deviceId;

    public string? UserId { get; set; } = userId;
}
