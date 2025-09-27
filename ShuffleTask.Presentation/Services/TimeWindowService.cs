using ShuffleTask.Models;

namespace ShuffleTask.Services;

public static class TimeWindowService
{
    // Returns true if nowLocal falls inside the [start, end) window in local time.
    // Handles overnight windows (e.g., start 22:00, end 06:00 -> spans midnight).
    public static bool IsWithinWorkHours(DateTime nowLocal, TimeSpan start, TimeSpan end)
    {
        TimeSpan t = nowLocal.TimeOfDay;

        // If start == end, treat as full-day window
        if (start == end)
            return true;

        if (start < end)
        {
            // Same-day window
            return t >= start && t < end;
        }
        else
        {
            // Overnight window (crosses midnight)
            // Example: 22:00 - 06:00 -> allowed if time is >= 22:00 OR < 06:00
            return t >= start || t < end;
        }
    }

    public static bool AllowedNow(AllowedPeriod ap, DateTime nowLocal, AppSettings s)
        => ap switch
        {
            AllowedPeriod.Any => true,
            AllowedPeriod.Work => IsWithinWorkHours(nowLocal, s.WorkStart, s.WorkEnd),
            AllowedPeriod.OffWork => !IsWithinWorkHours(nowLocal, s.WorkStart, s.WorkEnd),
            AllowedPeriod.Off => !IsWithinWorkHours(nowLocal, s.WorkStart, s.WorkEnd),
            _ => true,
        };

    // Compute minutes until the next work window boundary (open or close)
    public static TimeSpan UntilNextBoundary(DateTime nowLocal, AppSettings s)
    {
        if (s.WorkStart == s.WorkEnd)
        {
            return TimeSpan.Zero;
        }

        bool within = IsWithinWorkHours(nowLocal, s.WorkStart, s.WorkEnd);
        TimeSpan nextBoundaryTime = within ? s.WorkEnd : s.WorkStart;
        DateTime nextBoundary = GetNextOccurrence(nowLocal, nextBoundaryTime);

        return nextBoundary - nowLocal;
    }

    private static DateTime GetNextOccurrence(DateTime nowLocal, TimeSpan boundary)
    {
        DateTime candidate = nowLocal.Date + boundary;
        if (candidate <= nowLocal)
        {
            candidate = candidate.AddDays(1);
        }

        return candidate;
    }
}
