using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Services;

public static class TimeWindowService
{
    public static bool IsWeekend(DateTimeOffset now)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
        return local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    }

    // Returns true if nowLocal falls inside the [start, end) window in local time.
    // Handles overnight windows (e.g., start 22:00, end 06:00 -> spans midnight).
    public static bool IsWithinWorkHours(DateTimeOffset now, TimeSpan start, TimeSpan end)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
        TimeSpan t = local.TimeOfDay;

        // If start == end, treat as full-day window
        if (start == end)
        {
            return true;
        }

        if (start < end)
        {
            // Same-day window
            return t >= start && t < end;
        }

        // Overnight window (crosses midnight)
        // Example: 22:00 - 06:00 -> allowed if time is >= 22:00 OR < 06:00
        return t >= start || t < end;
    }

    public static bool AllowedNow(AllowedPeriod ap, DateTimeOffset now, AppSettings s)
    {
        PeriodDefinition definition = ap switch
        {
            AllowedPeriod.Work => PeriodDefinitionCatalog.Work,
            AllowedPeriod.OffWork => PeriodDefinitionCatalog.OffWork,
            _ => PeriodDefinitionCatalog.Any
        };

        return AllowedNow(definition, now, s);
    }

    public static bool AllowedNow(TaskItem task, DateTimeOffset now, AppSettings s)
    {
        ArgumentNullException.ThrowIfNull(task);

        PeriodDefinition definition = ResolveDefinition(task);
        return AllowedNow(definition, now, s);
    }

    public static bool AllowsWeekend(TaskItem task, AppSettings s)
    {
        ArgumentNullException.ThrowIfNull(task);

        PeriodDefinition definition = ResolveDefinition(task);
        return AllowsWeekend(definition, s);
    }

    public static bool AllowsWeekend(PeriodDefinition definition, AppSettings s)
    {
        ArgumentNullException.ThrowIfNull(definition);

        Weekdays weekdays = NormalizeWeekdays(definition.Weekdays);
        TimeSpan start = definition.StartTime ?? TimeSpan.Zero;
        TimeSpan end = definition.EndTime ?? TimeSpan.Zero;
        if (definition.Mode.HasFlag(PeriodDefinitionMode.AlignWithWorkHours))
        {
            start = s.WorkStart;
            end = s.WorkEnd;
        }

        if (weekdays.HasFlag(Weekdays.Sat) || weekdays.HasFlag(Weekdays.Sun))
        {
            return true;
        }

        return start > end && end > TimeSpan.Zero && weekdays.HasFlag(Weekdays.Fri);
    }

    public static bool AllowedNow(PeriodDefinition definition, DateTimeOffset now, AppSettings s)
    {
        ArgumentNullException.ThrowIfNull(definition);

        Weekdays weekdays = NormalizeWeekdays(definition.Weekdays);
        TimeSpan start = definition.StartTime ?? TimeSpan.Zero;
        TimeSpan end = definition.EndTime ?? TimeSpan.Zero;
        if (definition.Mode.HasFlag(PeriodDefinitionMode.AlignWithWorkHours))
        {
            start = s.WorkStart;
            end = s.WorkEnd;
        }

        if (!IsWithinWeekdayScope(now, weekdays, start, end))
        {
            return false;
        }

        if (definition.Mode.HasFlag(PeriodDefinitionMode.OffWorkRelativeToWorkHours))
        {
            if (IsWeekend(now))
            {
                return true;
            }

            return !IsWithinWorkHours(now, start, end);
        }

        return definition.IsAllDay || IsWithinWorkHours(now, start, end);
    }

    private static PeriodDefinition ResolveDefinition(TaskItem task)
    {
        if (PeriodDefinitionCatalog.TryGet(task.PeriodDefinitionId, out PeriodDefinition definition))
        {
            return definition;
        }

        if (TryBuildAdHocDefinition(task, out PeriodDefinition adHocDefinition))
        {
            return adHocDefinition;
        }

        return task.AllowedPeriod switch
        {
            AllowedPeriod.Work => PeriodDefinitionCatalog.Work,
            AllowedPeriod.OffWork => PeriodDefinitionCatalog.OffWork,
            AllowedPeriod.Custom => BuildLegacyCustomDefinition(task),
            _ => PeriodDefinitionCatalog.Any
        };
    }

    private static bool TryBuildAdHocDefinition(TaskItem task, out PeriodDefinition definition)
    {
        bool hasAdHocDefinition = task.AdHocStartTime.HasValue
            || task.AdHocEndTime.HasValue
            || task.AdHocWeekdays.HasValue
            || task.AdHocIsAllDay
            || task.AdHocMode != PeriodDefinitionMode.None;

        if (!hasAdHocDefinition)
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

    private static PeriodDefinition BuildLegacyCustomDefinition(TaskItem task)
    {
        bool isAllDay = !task.CustomStartTime.HasValue || !task.CustomEndTime.HasValue;

        return new PeriodDefinition
        {
            Id = string.Empty,
            Name = "Legacy custom",
            StartTime = task.CustomStartTime,
            EndTime = task.CustomEndTime,
            Weekdays = task.CustomWeekdays ?? PeriodDefinitionCatalog.AllWeekdays,
            IsAllDay = isAllDay,
            Mode = PeriodDefinitionMode.None
        };
    }

    private static Weekdays NormalizeWeekdays(Weekdays weekdays)
    {
        return weekdays == Weekdays.None ? PeriodDefinitionCatalog.AllWeekdays : weekdays;
    }

    private static bool IsWithinWeekdayScope(DateTimeOffset now, Weekdays weekdays, TimeSpan start, TimeSpan end)
    {
        if (weekdays == Weekdays.None)
        {
            return true;
        }

        DateTimeOffset local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
        DateTimeOffset weekdaySource = local;
        if (start > end && local.TimeOfDay < end)
        {
            weekdaySource = local.AddDays(-1);
        }

        Weekdays today = GetWeekdayFlag(weekdaySource);
        return weekdays.HasFlag(today);
    }

    // Check if auto-shuffle is allowed for a specific task at the current time.
    // Manual shuffle uses a separate candidate pool that always bypasses the AutoShuffleAllowed flag
    // and may optionally ignore AllowedPeriod based on user settings.
    public static bool AutoShuffleAllowedNow(TaskItem task, DateTimeOffset now, AppSettings s)
    {
        // If task explicitly disallows auto-shuffle, return false
        if (!task.AutoShuffleAllowed)
        {
            return false;
        }

        return AllowedNow(task, now, s);
    }

    private static Weekdays GetWeekdayFlag(DateTimeOffset now)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
        return local.DayOfWeek switch
        {
            DayOfWeek.Sunday => Weekdays.Sun,
            DayOfWeek.Monday => Weekdays.Mon,
            DayOfWeek.Tuesday => Weekdays.Tue,
            DayOfWeek.Wednesday => Weekdays.Wed,
            DayOfWeek.Thursday => Weekdays.Thu,
            DayOfWeek.Friday => Weekdays.Fri,
            DayOfWeek.Saturday => Weekdays.Sat,
            _ => Weekdays.None
        };
    }

    // Compute minutes until the next work window boundary (open or close)
    public static TimeSpan UntilNextBoundary(DateTimeOffset now, AppSettings s)
    {
        if (s.WorkStart == s.WorkEnd)
        {
            return TimeSpan.Zero;
        }

        bool within = IsWithinWorkHours(now, s.WorkStart, s.WorkEnd);
        TimeSpan nextBoundaryTime = within ? s.WorkEnd : s.WorkStart;
        DateTimeOffset nextBoundary = GetNextOccurrence(now, nextBoundaryTime);

        return nextBoundary - now;
    }

    public static DateTimeOffset NextWeekdayStart(DateTimeOffset now, TimeSpan start)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
        DateTime date = local.Date;

        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            date = date.AddDays(1);
        }

        DateTimeOffset candidate = CreateLocalCandidate(date, start);
        if (candidate <= local)
        {
            date = date.AddDays(1);
            while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                date = date.AddDays(1);
            }

            candidate = CreateLocalCandidate(date, start);
        }

        return candidate.ToOffset(TimeSpan.Zero);
    }

    private static DateTimeOffset CreateLocalCandidate(DateTime date, TimeSpan start)
    {
        DateTime localDateTime = date + start;
        TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(localDateTime);
        return new DateTimeOffset(localDateTime, offset);
    }

    private static DateTimeOffset GetNextOccurrence(DateTimeOffset now, TimeSpan boundary)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
        DateTimeOffset candidate = new DateTimeOffset(local.Date + boundary, local.Offset);
        if (candidate <= local)
        {
            candidate = candidate.AddDays(1);
        }

        return candidate.ToOffset(TimeSpan.Zero);
    }
}
