using ShuffleTask.Models;

namespace ShuffleTask.Services;

public static class ImportanceUrgencyCalculator
{
    private const double ImportanceWeightPoints = 60.0;
    private const double UrgencyWeightPoints = 40.0;
    private const double DeadlineShare = 0.75;
    private const double RepeatShare = 0.25;
    private const double RepeatPenalty = 0.6;
    private const double DefaultStoryPoints = 3.0;
    private const double MinStoryPoints = 0.5;
    private const double MaxStoryPoints = 13.0;
    private const double MinDeadlineWindowHours = 24.0;
    private const double MaxDeadlineWindowHours = 168.0;
    private const double SizeBiasStrength = 0.2;
    private const double SizeBiasMinMultiplier = 0.8;
    private const double SizeBiasMaxMultiplier = 1.2;

    public static ImportanceUrgencyScore Calculate(TaskItem task, DateTime nowLocal, AppSettings settings)
    {
        if (task == null)
            throw new ArgumentNullException(nameof(task));
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        double storyPoints = NormalizeStoryPoints(task.SizePoints);

        double importanceNorm = NormalizeImportance(task.Importance);
        double importanceWeighted = importanceNorm * ImportanceWeightPoints;

        double deadlineNorm = NormalizeDeadlineUrgency(task, nowLocal, storyPoints);
        double repeatNorm = NormalizeRepeatUrgency(task, nowLocal, settings);

        double urgencyDeadlinePoints = deadlineNorm * (UrgencyWeightPoints * DeadlineShare);
        double urgencyRepeatPoints = repeatNorm * (UrgencyWeightPoints * RepeatShare) * RepeatPenalty;

        double baseTotal = importanceWeighted + urgencyDeadlinePoints + urgencyRepeatPoints;
        double sizeMultiplier = ComputeSizeMultiplier(storyPoints);
        double total = baseTotal * sizeMultiplier;

        return new ImportanceUrgencyScore(
            WeightedImportance: importanceWeighted,
            WeightedDeadlineUrgency: urgencyDeadlinePoints,
            WeightedRepeatUrgency: urgencyRepeatPoints,
            SizeMultiplier: sizeMultiplier,
            CombinedScore: total);
    }

    private static double NormalizeImportance(int importance)
    {
        double clamped = Math.Max(1, Math.Min(5, importance));
        return (clamped - 1.0) / 4.0;
    }

    private static double NormalizeDeadlineUrgency(TaskItem task, DateTime nowLocal, double storyPoints)
    {
        if (task.Deadline == null)
        {
            return 0.05;
        }

        TimeSpan diff = task.Deadline.Value - nowLocal;
        double hours = diff.TotalHours;

        if (hours >= 0)
        {
            double window = ComputeDeadlineWindowHours(storyPoints);
            double urgency = 1.0 - Math.Min(1.0, hours / window);
            return Clamp01(urgency);
        }

        double overdueHours = Math.Abs(hours);
        double overdueBoost = Math.Min(0.5, overdueHours / 24.0 * 0.25);
        return 1.0 + overdueBoost;
    }

    private static double NormalizeRepeatUrgency(TaskItem task, DateTime nowLocal, AppSettings settings)
    {
        double baseUrgency = task.Repeat switch
        {
            RepeatType.None => 0.0,
            RepeatType.Daily => ComputeDailyUrgency(task, nowLocal),
            RepeatType.Weekly => ComputeWeeklyUrgency(task, nowLocal),
            RepeatType.Interval => ComputeIntervalUrgency(task, nowLocal),
            _ => 0.0
        };

        if (baseUrgency <= 0)
        {
            return 0.0;
        }

        double daysSince = task.LastDoneAt.HasValue
            ? Math.Max(0.0, (nowLocal - task.LastDoneAt.Value).TotalDays)
            : 7.0;
        double streakBias = Clamp01(settings.StreakBias);
        double streakMultiplier = 1.0 + (streakBias * Math.Min(7.0, daysSince) / 7.0);

        double urgencyWithStreak = baseUrgency * streakMultiplier;
        return Math.Min(1.0, urgencyWithStreak);
    }

    private static double ComputeDailyUrgency(TaskItem task, DateTime nowLocal)
    {
        if (task.LastDoneAt == null)
        {
            return 0.6;
        }

        double hours = (nowLocal - task.LastDoneAt.Value).TotalHours;
        if (hours <= 0)
        {
            return 0.1;
        }

        return Math.Min(1.0, hours / 24.0);
    }

    private static double ComputeWeeklyUrgency(TaskItem task, DateTime nowLocal)
    {
        Weekdays today = DayToWeekdayFlag(nowLocal.DayOfWeek);
        bool plannedToday = (task.Weekdays & today) != 0;

        if (!plannedToday)
        {
            return 0.2;
        }

        if (task.LastDoneAt == null)
        {
            return 0.7;
        }

        double days = (nowLocal - task.LastDoneAt.Value).TotalDays;
        if (days <= 0)
        {
            return 0.2;
        }

        return Math.Min(1.0, days / 7.0);
    }

    private static double ComputeIntervalUrgency(TaskItem task, DateTime nowLocal)
    {
        int interval = Math.Max(1, task.IntervalDays);
        if (task.LastDoneAt == null)
        {
            return 0.5;
        }

        double days = (nowLocal - task.LastDoneAt.Value).TotalDays;
        if (days <= 0)
        {
            return 0.1;
        }

        if (days <= interval)
        {
            return Math.Min(0.6, days / interval * 0.6);
        }

        double overdue = days - interval;
        double urgency = 0.6 + Math.Min(0.4, overdue / interval * 0.4);
        return Math.Min(1.0, urgency);
    }

    private static Weekdays DayToWeekdayFlag(DayOfWeek day)
        => day switch
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

    private static double NormalizeStoryPoints(double rawPoints)
    {
        if (double.IsNaN(rawPoints) || double.IsInfinity(rawPoints) || rawPoints <= 0)
        {
            return DefaultStoryPoints;
        }

        if (rawPoints < MinStoryPoints)
        {
            return MinStoryPoints;
        }

        if (rawPoints > MaxStoryPoints)
        {
            return MaxStoryPoints;
        }

        return rawPoints;
    }

    private static double ComputeDeadlineWindowHours(double storyPoints)
    {
        double scaled = 72.0 * (storyPoints / DefaultStoryPoints);

        if (scaled < MinDeadlineWindowHours)
        {
            return MinDeadlineWindowHours;
        }

        if (scaled > MaxDeadlineWindowHours)
        {
            return MaxDeadlineWindowHours;
        }

        return scaled;
    }

    private static double ComputeSizeMultiplier(double storyPoints)
    {
        double normalized = storyPoints / DefaultStoryPoints;
        double bias = 1.0 + (SizeBiasStrength * (1.0 - normalized));

        if (bias < SizeBiasMinMultiplier)
        {
            return SizeBiasMinMultiplier;
        }

        if (bias > SizeBiasMaxMultiplier)
        {
            return SizeBiasMaxMultiplier;
        }

        return bias;
    }

    private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));
}

public readonly record struct ImportanceUrgencyScore(
    double WeightedImportance,
    double WeightedDeadlineUrgency,
    double WeightedRepeatUrgency,
    double SizeMultiplier,
    double CombinedScore)
{
    public double WeightedUrgency => WeightedDeadlineUrgency + WeightedRepeatUrgency;
}
