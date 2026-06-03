using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Models;

public sealed class SyncTaskBatch
{
    public SyncTaskBatch(
        string peerId,
        string? userId,
        string deviceId,
        IEnumerable<TaskItem> tasks,
        IEnumerable<string>? deletedTaskIds = null)
    {
        PeerId = string.IsNullOrWhiteSpace(peerId) ? string.Empty : peerId.Trim();
        UserId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
        DeviceId = string.IsNullOrWhiteSpace(deviceId) ? string.Empty : deviceId.Trim();
        Tasks = tasks?.Select(TaskItem.Clone).ToArray() ?? Array.Empty<TaskItem>();
        DeletedTaskIds = deletedTaskIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray()
            ?? Array.Empty<string>();
    }

    public string PeerId { get; }

    public string? UserId { get; }

    public string DeviceId { get; }

    public IReadOnlyCollection<TaskItem> Tasks { get; }

    public IReadOnlyCollection<string> DeletedTaskIds { get; }
}

public sealed record SyncApplyResult(
    IReadOnlyCollection<string> AppliedTaskIds,
    IReadOnlyCollection<string> IgnoredTaskIds);
