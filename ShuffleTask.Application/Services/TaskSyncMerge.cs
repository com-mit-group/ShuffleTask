using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Services;

internal static class TaskSyncMerge
{
    public static TaskItem NormalizeIncoming(TaskItem task, TaskItem? existing)
    {
        var normalized = task.Clone();

        if (string.IsNullOrWhiteSpace(normalized.Id))
        {
            normalized.Id = existing?.Id ?? Guid.NewGuid().ToString("n");
        }

        if (normalized.CreatedAt == default)
        {
            normalized.CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow;
        }

        if (normalized.UpdatedAt == default)
        {
            normalized.UpdatedAt = existing?.UpdatedAt ?? DateTime.UtcNow;
        }

        if (normalized.EventVersion <= 0)
        {
            normalized.EventVersion = (existing?.EventVersion ?? 0) + 1;
        }

        if (!string.IsNullOrWhiteSpace(normalized.UserId))
        {
            normalized.DeviceId = null;
        }
        else
        {
            normalized.UserId = existing?.UserId;
            normalized.DeviceId = string.IsNullOrWhiteSpace(normalized.DeviceId)
                ? existing?.DeviceId ?? Environment.MachineName
                : normalized.DeviceId.Trim();
        }

        return normalized;
    }

    public static bool IsStaleBatchTask(TaskItem incoming, TaskItem existing)
    {
        if (incoming.EventVersion > existing.EventVersion)
        {
            return false;
        }

        if (incoming.EventVersion < existing.EventVersion)
        {
            return true;
        }

        if (incoming.UpdatedAt != default && existing.UpdatedAt != default)
        {
            return incoming.UpdatedAt <= existing.UpdatedAt;
        }

        return true;
    }

    public static bool IsStaleEventTask(TaskItem incoming, TaskItem existing)
    {
        if (incoming.EventVersion > 0)
        {
            return incoming.EventVersion <= existing.EventVersion;
        }

        return incoming.UpdatedAt != default && incoming.UpdatedAt <= existing.UpdatedAt;
    }
}
