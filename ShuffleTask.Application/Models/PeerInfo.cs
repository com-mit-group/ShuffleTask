namespace ShuffleTask.Application.Models;

public sealed record PeerInfo(
    string DeviceId,
    string? UserId,
    string? SessionId,
    DateTimeOffset? LastSeen,
    string? ConnectionState);
