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
        PeerId = SyncIdentity.Required(peerId);
        UserId = SyncIdentity.Optional(userId);
        DeviceId = SyncIdentity.Required(deviceId);
        Tasks = tasks?.Select(TaskItem.Clone).ToArray() ?? Array.Empty<TaskItem>();
        DeletedTaskIds = SyncIdentity.DistinctIds(deletedTaskIds);
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
