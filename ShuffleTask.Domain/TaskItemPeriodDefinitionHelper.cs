namespace ShuffleTask.Domain.Entities;

public static class TaskItemPeriodDefinitionHelper
{
    public static bool HasAdHocDefinition(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return task.AdHocStartTime.HasValue
            || task.AdHocEndTime.HasValue
            || task.AdHocWeekdays.HasValue
            || task.AdHocIsAllDay
            || task.AdHocMode != PeriodDefinitionMode.None;
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
}
