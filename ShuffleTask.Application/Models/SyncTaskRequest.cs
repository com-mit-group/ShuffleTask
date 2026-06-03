namespace ShuffleTask.Application.Models;

public sealed class SyncTaskRequest
{
    public SyncTaskRequest(string peerId, string? userId, string deviceId, IEnumerable<string> requestedTaskIds)
    {
        PeerId = SyncIdentity.Required(peerId);
        UserId = SyncIdentity.Optional(userId);
        DeviceId = SyncIdentity.Required(deviceId);
        RequestedTaskIds = SyncIdentity.DistinctIds(requestedTaskIds);
    }

    public string PeerId { get; }

    public string? UserId { get; }

    public string DeviceId { get; }

    public IReadOnlyCollection<string> RequestedTaskIds { get; }
}
