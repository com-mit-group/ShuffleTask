namespace ShuffleTask.Application.Models;

public sealed record SyncPeerContext(string PeerId, string? UserId, string DeviceId)
{
    public string PeerId { get; init; } = SyncIdentity.Required(PeerId);

    public string? UserId { get; init; } = SyncIdentity.Optional(UserId);

    public string DeviceId { get; init; } = SyncIdentity.Required(DeviceId);
}
