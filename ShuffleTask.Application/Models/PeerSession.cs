namespace ShuffleTask.Application.Models;

public sealed record PeerSession(
    string PeerId,
    string SharedSecret,
    DateTime LastSeenUtc,
    IReadOnlyCollection<string> TransportCapabilities,
    string? UserId = null,
    string? DeviceId = null,
    string? RendezvousRoomId = null);
