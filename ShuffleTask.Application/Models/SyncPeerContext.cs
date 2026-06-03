namespace ShuffleTask.Application.Models;

public sealed record SyncPeerContext(string PeerId, string? UserId, string DeviceId)
{
    public string PeerId { get; init; } = string.IsNullOrWhiteSpace(PeerId) ? string.Empty : PeerId.Trim();

    public string? UserId { get; init; } = string.IsNullOrWhiteSpace(UserId) ? null : UserId.Trim();

    public string DeviceId { get; init; } = string.IsNullOrWhiteSpace(DeviceId) ? string.Empty : DeviceId.Trim();
}
