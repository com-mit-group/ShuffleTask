namespace ShuffleTask.Application.Models;

public abstract class SyncPeerMessage
{
    protected SyncPeerMessage(string peerId, string? userId, string deviceId)
    {
        PeerId = SyncIdentity.Required(peerId);
        UserId = SyncIdentity.Optional(userId);
        DeviceId = SyncIdentity.Required(deviceId);
    }

    public string PeerId { get; }

    public string? UserId { get; }

    public string DeviceId { get; }
}
