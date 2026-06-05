namespace ShuffleTask.Application.Models;

public sealed class SyncTaskRequest : SyncPeerMessage
{
    public SyncTaskRequest(string peerId, string? userId, string deviceId, IEnumerable<string> requestedTaskIds)
        : base(peerId, userId, deviceId)
    {
        RequestedTaskIds = SyncIdentity.DistinctIds(requestedTaskIds);
    }

    public IReadOnlyCollection<string> RequestedTaskIds { get; }
}
