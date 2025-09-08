using ShuffleTask.Models;

namespace ShuffleTask.Services;

public class SchedulerService
{
    private readonly bool _deterministic;

    public SchedulerService(bool deterministic = false)
    {
        _deterministic = deterministic;
    }

    public TimeSpan NextGap(AppSettings s, DateTime nowLocal)
    {
        int min = Math.Max(0, s.MinGapMinutes);
        int max = Math.Max(min, s.MaxGapMinutes);

        if (_deterministic)
        {
            // Deterministic: return midpoint
            int mid = (min + max) / 2;
            return TimeSpan.FromMinutes(mid);
        }

        // Pick RNG based on settings
        Random rng;
        if (s.StableRandomnessPerDay)
        {
            int seed = nowLocal.Year * 10000 + nowLocal.Month * 100 + nowLocal.Day;
            rng = new Random(seed ^ 0x5f3759df);
        }
        else
        {
            rng = new Random();
        }

        int range = Math.Max(1, (max - min + 1));
        int minutes = min + rng.Next(0, range);
        return TimeSpan.FromMinutes(minutes);
    }

    public TaskItem? PickNextTask(IEnumerable<TaskItem> tasks, AppSettings s, DateTime nowLocal)
        => SchedulerService.PickNextTask(tasks, s, nowLocal, _deterministic);

    public static TaskItem? PickNextTask(IEnumerable<TaskItem> tasks, AppSettings s, DateTime nowLocal, bool deterministic)
    {
        if (tasks == null) return null;

        var candidates = tasks
            .Where(t => t is not null)
            .Where(t => !t.Paused)
            .Where(t => TimeWindowService.AllowedNow(t.AllowedPeriod, nowLocal, s))
            .ToList();

        if (candidates.Count == 0)
            return null;

        // Compute scores
        var scored = new List<(TaskItem Task, double Score)>();

        foreach (var t in candidates)
        {
            double importance = Clamp(t.Importance, 1, 5);
            double deadlineUrgency = ComputeDeadlineUrgency(t, nowLocal);
            double repeatUrgency = ComputeRepeatUrgency(t, nowLocal);

            // Streak bias: 1 + bias * min(7, daysSince)/7
            double daysSince = t.LastDoneAt.HasValue ? (nowLocal - t.LastDoneAt.Value).TotalDays : 7.0;
            double streakFactor = 1.0 + (Clamp01(s.StreakBias) * Math.Min(7.0, Math.Max(0.0, daysSince)) / 7.0);
            repeatUrgency *= streakFactor;

            double score = 1.0 * importance + 1.5 * deadlineUrgency + 1.2 * repeatUrgency;

            if (!deterministic)
            {
                // jitter in [-0.5, 0.5]
                Random rng;
                if (s.StableRandomnessPerDay)
                {
                    int seed = nowLocal.Year * 10000 + nowLocal.Month * 100 + nowLocal.Day;
                    unchecked { seed = seed * 31 + t.Id.GetHashCode(); }
                    rng = new Random(seed);
                }
                else
                {
                    rng = new Random();
                }
                score += (rng.NextDouble() - 0.5);
            }

            scored.Add((Task: t, Score: score));
        }

        if (deterministic)
        {
            // Deterministic: pick argmax (stable)
            return scored
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Task.Id, StringComparer.Ordinal)
                .First().Task;
        }

        // Softmax sampling
        double maxScore = scored.Max(x => x.Score);
        var expScores = scored.Select(x => Math.Exp(x.Score - maxScore)).ToArray();
        double sum = expScores.Sum();
        if (sum <= 0)
        {
            // Fallback: pick max
            return scored.OrderByDescending(x => x.Score).First().Task;
        }

        double[] cumulative = new double[expScores.Length];
        double acc = 0;
        for (int i = 0; i < expScores.Length; i++)
        {
            acc += expScores[i];
            cumulative[i] = acc;
        }

        Random sampler;
        if (s.StableRandomnessPerDay)
        {
            int daySeed = nowLocal.Year * 10000 + nowLocal.Month * 100 + nowLocal.Day;
            int seeded = unchecked(daySeed ^ (int)0x9e3779b9);
            sampler = new Random(seeded);
        }
        else
        {
            sampler = new Random();
        }

        double r = sampler.NextDouble() * sum;

        for (int i = 0; i < cumulative.Length; i++)
        {
            if (r <= cumulative[i])
                return scored[i].Task;
        }

        return scored.Last().Task;
    }

    private static double ComputeDeadlineUrgency(TaskItem t, DateTime nowLocal)
    {
        if (t.Repeat != RepeatType.None)
            return 0; // deadlines considered mostly for non-repeating in this simple model

        if (t.Deadline == null)
        {
            // No deadline -> low baseline
            return 0.3;
        }

        DateTime dl = t.Deadline.Value;
        TimeSpan diff = dl - nowLocal;
        double hours = diff.TotalHours;

        if (hours >= 0)
        {
            // Approaching deadline: map 72h -> 0 up to 0h -> 1
            double urgency = 1.0 - (hours / 72.0);
            return Clamp01(urgency);
        }
        else
        {
            // Past deadline -> strong urgency growing with time, cap at 2
            double overdueHours = -hours;
            return Math.Min(2.0, 1.0 + overdueHours / 24.0);
        }
    }

    private static double ComputeRepeatUrgency(TaskItem t, DateTime nowLocal)
    {
        switch (t.Repeat)
        {
            case RepeatType.None:
                return 0;
            case RepeatType.Daily:
            {
                if (t.LastDoneAt == null)
                    return 0.7; // never done -> medium
                double hours = (nowLocal - t.LastDoneAt.Value).TotalHours;
                return Math.Min(2.0, Math.Max(0, hours / 24.0));
            }
            case RepeatType.Weekly:
            {
                Weekdays todayFlag = DayToWeekdayFlag(nowLocal.DayOfWeek);
                bool todayPlanned = (t.Weekdays & todayFlag) != 0;
                if (!todayPlanned)
                    return 0.1; // small baseline if not today

                if (t.LastDoneAt == null)
                    return 0.8; // never done -> medium-high on planned day

                double days = (nowLocal - t.LastDoneAt.Value).TotalDays;
                return Math.Min(2.0, Math.Max(0, days / 7.0));
            }
            case RepeatType.Interval:
            {
                int n = Math.Max(1, t.IntervalDays);
                if (t.LastDoneAt == null)
                    return 0.6; // never done -> medium
                double days = (nowLocal - t.LastDoneAt.Value).TotalDays;
                if (days <= n)
                    return Math.Max(0.0, days / n * 0.5);
                return Math.Min(2.0, 0.5 + (days - n) / n);
            }
            default:
                return 0;
        }
    }

    private static Weekdays DayToWeekdayFlag(DayOfWeek dow)
    {
        return dow switch
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

    private static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));
    private static double Clamp01(double v) => Math.Max(0, Math.Min(1, v));
}
