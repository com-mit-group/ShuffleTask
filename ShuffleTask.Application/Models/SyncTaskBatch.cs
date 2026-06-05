using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Models;

public sealed class SyncTaskBatch : SyncPeerMessage
{
    public SyncTaskBatch(
        string peerId,
        string? userId,
        string deviceId,
        IEnumerable<TaskItem> tasks,
        IEnumerable<string>? deletedTaskIds = null)
        : base(peerId, userId, deviceId)
    {
        Tasks = tasks?.Select(TaskItem.Clone).ToArray() ?? Array.Empty<TaskItem>();
        DeletedTaskIds = SyncIdentity.DistinctIds(deletedTaskIds);
    }

    public IReadOnlyCollection<TaskItem> Tasks { get; }

    public IReadOnlyCollection<string> DeletedTaskIds { get; }
}

public sealed record SyncApplyResult(
    IReadOnlyCollection<string> AppliedTaskIds,
    IReadOnlyCollection<string> IgnoredTaskIds);
