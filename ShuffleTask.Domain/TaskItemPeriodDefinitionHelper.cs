namespace ShuffleTask.Domain.Entities;

public static class TaskItemPeriodDefinitionHelper
{
    public static bool HasAdHocDefinition(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return HasAdHocDefinition((TaskItemData)task);
    }

    public static bool TryBuildAdHocDefinition(TaskItem task, out PeriodDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (!HasAdHocDefinition(task))
        {
            definition = PeriodDefinitionCatalog.Any;
            return false;
        }

        definition = new PeriodDefinition
        {
            Id = string.Empty,
            Name = "Ad-hoc",
            StartTime = task.AdHocStartTime,
            EndTime = task.AdHocEndTime,
            Weekdays = task.AdHocWeekdays ?? PeriodDefinitionCatalog.AllWeekdays,
            IsAllDay = task.AdHocIsAllDay,
            Mode = task.AdHocMode
        };
        return true;
    }

    public static void NormalizeLegacyPeriodDefinition(TaskItemData task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (!string.IsNullOrWhiteSpace(task.PeriodDefinitionId) || HasAdHocDefinition(task))
        {
            return;
        }

        if (task.AllowedPeriod == AllowedPeriod.Custom)
        {
            if (task.CustomStartTime.HasValue || task.CustomEndTime.HasValue || task.CustomWeekdays.HasValue)
            {
                task.AdHocStartTime = task.CustomStartTime;
                task.AdHocEndTime = task.CustomEndTime;
                task.AdHocWeekdays = task.CustomWeekdays;
                task.AdHocIsAllDay = !task.CustomStartTime.HasValue || !task.CustomEndTime.HasValue;
                task.AdHocMode = PeriodDefinitionMode.None;
            }

            return;
        }

        task.PeriodDefinitionId = task.AllowedPeriod switch
        {
            AllowedPeriod.Work => PeriodDefinitionCatalog.WorkId,
            AllowedPeriod.OffWork => PeriodDefinitionCatalog.OffWorkId,
            _ => PeriodDefinitionCatalog.AnyId
        };
    }

    private static bool HasAdHocDefinition(TaskItemData task)
    {
        return task.AdHocStartTime.HasValue
            || task.AdHocEndTime.HasValue
            || task.AdHocWeekdays.HasValue
            || task.AdHocIsAllDay
            || task.AdHocMode != PeriodDefinitionMode.None;
    }
}
