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
            AllowedPeriod.Off => !IsWithinWorkHours(now, s.WorkStart, s.WorkEnd),
            _ => true,
        };

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
