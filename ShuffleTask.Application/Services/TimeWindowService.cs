using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Services;

public static class TimeWindowService
{
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
        => ap switch
        {
            AllowedPeriod.Any => true,
            AllowedPeriod.Work => IsWithinWorkHours(now, s.WorkStart, s.WorkEnd),
            AllowedPeriod.OffWork => !IsWithinWorkHours(now, s.WorkStart, s.WorkEnd),
            _ => true,
        };

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

        // Check the allowed period
        return task.AllowedPeriod switch
        {
            AllowedPeriod.Any => true,
            AllowedPeriod.Work => IsWithinWorkHours(now, s.WorkStart, s.WorkEnd),
            AllowedPeriod.OffWork => !IsWithinWorkHours(now, s.WorkStart, s.WorkEnd),
            AllowedPeriod.Custom => IsWithinCustomHours(now, task.CustomStartTime, task.CustomEndTime),
            _ => true,
        };
    }

    // Check if current time is within custom hours defined for a task
    private static bool IsWithinCustomHours(DateTimeOffset now, TimeSpan? start, TimeSpan? end)
    {
        // If custom times are not set, default to allowing
        if (!start.HasValue || !end.HasValue)
        {
            return true;
        }

        return IsWithinWorkHours(now, start.Value, end.Value);
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
