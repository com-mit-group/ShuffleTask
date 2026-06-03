namespace ShuffleTask.Application.Models;

public sealed class SyncTaskRequest
{
    public SyncTaskRequest(string peerId, string? userId, string deviceId, IEnumerable<string> requestedTaskIds)
    {
        PeerId = string.IsNullOrWhiteSpace(peerId) ? string.Empty : peerId.Trim();
        UserId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
        DeviceId = string.IsNullOrWhiteSpace(deviceId) ? string.Empty : deviceId.Trim();
        RequestedTaskIds = requestedTaskIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();
    }

    public string PeerId { get; }

    public string? UserId { get; }

    public string DeviceId { get; }

    public IReadOnlyCollection<string> RequestedTaskIds { get; }
}
