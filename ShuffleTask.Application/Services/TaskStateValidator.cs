using ShuffleTask.Application.Utilities;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Services;

/// <summary>
/// Validates task state consistency and provides state transition validation
/// </summary>
public static class TaskStateValidator
{
    /// <summary>
    /// Validates that a task's state is consistent with its properties
    /// </summary>
    public static bool IsValidState(TaskItem task, DateTimeOffset now)
    {
        if (task == null) return false;

        return task.Status switch
        {
            TaskLifecycleStatus.Active => ValidateActiveState(task),
            TaskLifecycleStatus.Snoozed => ValidateSnoozedState(task, now),
            TaskLifecycleStatus.Completed => ValidateCompletedState(task, now),
            _ => false
        };
    }

    /// <summary>
    /// Validates that a state transition is allowed
    /// </summary>
    public static bool IsValidTransition(TaskLifecycleStatus from, TaskLifecycleStatus to)
    {
        return from switch
        {
            TaskLifecycleStatus.Active => to == TaskLifecycleStatus.Snoozed || to == TaskLifecycleStatus.Completed,
            TaskLifecycleStatus.Snoozed => to == TaskLifecycleStatus.Active || to == TaskLifecycleStatus.Completed,
            TaskLifecycleStatus.Completed => to == TaskLifecycleStatus.Active, // For repeating tasks
            _ => false
        };
    }

    /// <summary>
    /// Gets a human-readable description of a state transition
    /// </summary>
    public static string GetTransitionDescription(TaskLifecycleStatus from, TaskLifecycleStatus to)
    {
        return (from, to) switch
        {
            (TaskLifecycleStatus.Active, TaskLifecycleStatus.Snoozed) => "Task snoozed by user",
            (TaskLifecycleStatus.Active, TaskLifecycleStatus.Completed) => "Task completed by user",
            (TaskLifecycleStatus.Snoozed, TaskLifecycleStatus.Active) => "Task auto-resumed from snooze",
            (TaskLifecycleStatus.Snoozed, TaskLifecycleStatus.Completed) => "Snoozed task completed",
            (TaskLifecycleStatus.Completed, TaskLifecycleStatus.Active) => "Repeating task became active",
            _ => "Unknown transition"
        };
    }

    private static bool ValidateActiveState(TaskItem task)
    {
        // Active tasks should not have snooze or completion timestamps
        return task.SnoozedUntil == null && task.CompletedAt == null;
    }

    private static bool ValidateSnoozedState(TaskItem task, DateTimeOffset now)
    {
        // Snoozed tasks should have a snooze timestamp and NextEligibleAt
        if (!task.SnoozedUntil.HasValue || !task.NextEligibleAt.HasValue || task.CompletedAt != null)
        {
            return false;
        }

        DateTime? nextEligibleUtc = UtilityMethods.EnsureUtc(task.NextEligibleAt)?.UtcDateTime;
        return nextEligibleUtc.HasValue && nextEligibleUtc > now.UtcDateTime;
    }

    private static bool ValidateCompletedState(TaskItem task, DateTimeOffset now)
    {
        // Completed tasks should have a completion timestamp
        if (!task.CompletedAt.HasValue || task.SnoozedUntil != null)
        {
            return false;
        }

        if (!task.NextEligibleAt.HasValue)
        {
            return true;
        }

        DateTime? nextEligibleUtc = UtilityMethods.EnsureUtc(task.NextEligibleAt)?.UtcDateTime;
        return nextEligibleUtc.HasValue && nextEligibleUtc > now.UtcDateTime;
    }
}
