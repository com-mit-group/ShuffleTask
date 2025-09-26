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
        var within = IsWithinWorkHours(nowLocal, s.WorkStart, s.WorkEnd);
        DateTime todayStart = nowLocal.Date + s.WorkStart;
        DateTime todayEnd = nowLocal.Date + s.WorkEnd;

        // Handle overnight windows
        if (s.WorkStart > s.WorkEnd)
        {
            // Window spans midnight: start today to end tomorrow
            if (within)
            {
                // Next boundary is end today+1
                var nextEnd = nowLocal.Date.AddDays(nowLocal.TimeOfDay >= s.WorkStart ? 1 : 0) + s.WorkEnd;
                if (nextEnd <= nowLocal) nextEnd = nextEnd.AddDays(1);
                return nextEnd - nowLocal;
            }
            else
            {
                // Next boundary is start (could be today or tomorrow)
                var nextStart = nowLocal.TimeOfDay < s.WorkStart ? todayStart : todayStart.AddDays(1);
                if (nextStart <= nowLocal) nextStart = nextStart.AddDays(1);
                return nextStart - nowLocal;
            }
        }
        else
        {
            if (within)
            {
                if (nowLocal < todayEnd)
                    return todayEnd - nowLocal;
                // past end, next open is tomorrow start
                return (todayStart.AddDays(1)) - nowLocal;
            }
            else
            {
                if (nowLocal < todayStart)
                    return todayStart - nowLocal;
                // after end, next start is tomorrow
                return (todayStart.AddDays(1)) - nowLocal;
            }
        }
    }
}
