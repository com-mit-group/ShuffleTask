using ShuffleTask.Application.Models;
using ShuffleTask.Application.Utilities;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Services;

public static class ImportanceUrgencyCalculator
{
    private const double DefaultImportanceWeightPoints = 60.0;
    private const double DefaultUrgencyWeightPoints = 40.0;
    private const double DefaultDeadlineSharePercent = 75.0;
    private const double DefaultRepeatPenalty = 0.6;
    private const double MaxRepeatPenalty = 2.0;
    private const double DefaultStoryPoints = 3.0;
    private const double MinStoryPoints = 0.5;
    private const double MaxStoryPoints = 13.0;
    private const double MinDeadlineWindowHours = 24.0;
    private const double MaxDeadlineWindowHours = 168.0;
    private const double DefaultSizeBiasStrength = 0.2;
    private const double MaxSizeBiasStrength = 1.0;
    private const double SizeBiasMinMultiplier = 0.8;
    private const double SizeBiasMaxMultiplier = 1.2;

    public static ImportanceUrgencyScore Calculate(TaskItem task, DateTimeOffset now, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(settings);

        double storyPoints = NormalizeStoryPoints(task.SizePoints);
        DateTimeOffset nowLocal = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);

        double importanceNorm = NormalizeImportance(task.Importance);
        double importanceWeight = GetImportanceWeight(settings);
        double urgencyWeight = GetUrgencyWeight(settings);
        double deadlineShare = GetDeadlineShare(settings);
        double repeatShare = GetRepeatShare(deadlineShare);
        double repeatPenalty = GetRepeatPenalty(settings);
        double sizeBiasStrength = GetSizeBiasStrength(settings);

        double importanceWeighted = importanceNorm * importanceWeight;

        double deadlineNorm = NormalizeDeadlineUrgency(task, now, storyPoints);
        double repeatNorm = NormalizeRepeatUrgency(task, now, nowLocal, settings);

        double urgencyDeadlinePoints = deadlineNorm * (urgencyWeight * deadlineShare);
        double urgencyRepeatPoints = repeatNorm * (urgencyWeight * repeatShare) * repeatPenalty;

        double baseTotal = importanceWeighted + urgencyDeadlinePoints + urgencyRepeatPoints;
        double sizeMultiplier = ComputeSizeMultiplier(storyPoints, sizeBiasStrength);
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
        double clamped = Math.Clamp(importance, 1, 5);
        return (clamped - 1.0) / 4.0;
    }

    private static double NormalizeDeadlineUrgency(TaskItem task, DateTimeOffset now, double storyPoints)
    {
        DateTimeOffset? deadline = UtilityMethods.EnsureUtc(task.Deadline);
        if (deadline == null)
        {
            return 0.05;
        }

        TimeSpan diff = deadline.Value - now;
        double hours = diff.TotalHours;

        if (hours >= 0)
        {
            double window = ComputeDeadlineWindowHours(storyPoints);
            double urgency = 1.0 - Math.Min(1.0, hours / window);
            return UtilityMethods.Clamp01(urgency);
        }

        double overdueHours = Math.Abs(hours);
        double overdueBoost = Math.Min(0.5, overdueHours / 24.0 * 0.25);
        return 1.0 + overdueBoost;
    }

    private static double NormalizeRepeatUrgency(TaskItem task, DateTimeOffset now, DateTimeOffset nowLocal, AppSettings settings)
    {
        double baseUrgency = task.Repeat switch
        {
            RepeatType.None => 0.0,
            RepeatType.Daily => ComputeDailyUrgency(task, now),
            RepeatType.Weekly => ComputeWeeklyUrgency(task, nowLocal),
            RepeatType.Interval => ComputeIntervalUrgency(task, now),
            _ => 0.0
        };

        if (baseUrgency <= 0)
        {
            return 0.0;
        }

        DateTimeOffset? lastDone = UtilityMethods.EnsureUtc(task.LastDoneAt);
        double daysSince = lastDone.HasValue
            ? Math.Max(0.0, (now - lastDone.Value).TotalDays)
            : 7.0;
        double streakBias = UtilityMethods.Clamp01(settings.StreakBias);
        double streakMultiplier = 1.0 + (streakBias * Math.Min(7.0, daysSince) / 7.0);

        double urgencyWithStreak = baseUrgency * streakMultiplier;
        return Math.Min(1.0, urgencyWithStreak);
    }

    private static double ComputeDailyUrgency(TaskItem task, DateTimeOffset now)
    {
        DateTimeOffset? lastDone = UtilityMethods.EnsureUtc(task.LastDoneAt);
        if (lastDone == null)
        {
            return 0.6;
        }

        double hours = (now - lastDone.Value).TotalHours;
        if (hours <= 0)
        {
            return 0.1;
        }

        return Math.Min(1.0, hours / 24.0);
    }

    private static double ComputeWeeklyUrgency(TaskItem task, DateTimeOffset nowLocal)
    {
        Weekdays today = DayToWeekdayFlag(nowLocal.DayOfWeek);
        bool plannedToday = (task.Weekdays & today) != 0;

        if (!plannedToday)
        {
            return 0.2;
        }

        DateTimeOffset? lastDone = UtilityMethods.EnsureUtc(task.LastDoneAt);
        if (lastDone == null)
        {
            return 0.7;
        }

        double days = (nowLocal - lastDone.Value).TotalDays;
        if (days <= 0)
        {
            return 0.2;
        }

        return Math.Min(1.0, days / 7.0);
    }

    private static double ComputeIntervalUrgency(TaskItem task, DateTimeOffset now)
    {
        int interval = Math.Max(1, task.IntervalDays);
        DateTimeOffset? lastDone = UtilityMethods.EnsureUtc(task.LastDoneAt);
        if (lastDone == null)
        {
            return 0.5;
        }

        double days = (now - lastDone.Value).TotalDays;
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

    private static double ComputeSizeMultiplier(double storyPoints, double sizeBiasStrength)
    {
        double normalized = storyPoints / DefaultStoryPoints;
        double bias = 1.0 + (sizeBiasStrength * (normalized - 1.0));

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

    private static double GetImportanceWeight(AppSettings settings) => WeightOrDefault(settings.ImportanceWeight, DefaultImportanceWeightPoints);

    private static double WeightOrDefault(double weight, double defaultValue)
    {
        return UtilityMethods.IsInvalid(weight) || weight < 0.0
               ? defaultValue
               : weight;
    }

    private static double GetUrgencyWeight(AppSettings settings) => WeightOrDefault(settings.UrgencyWeight, DefaultUrgencyWeightPoints);

    private static double GetDeadlineShare(AppSettings settings)
    {
        double sharePercent = settings.UrgencyDeadlineShare;
        if (UtilityMethods.IsInvalid(sharePercent))
        {
            sharePercent = DefaultDeadlineSharePercent;
        }

        sharePercent = Math.Clamp(sharePercent, 0.0, 100.0);
        return sharePercent / 100.0;
    }

    private static double GetRepeatShare(double deadlineShare)
    {
        double clamped = UtilityMethods.Clamp01(deadlineShare);
        return UtilityMethods.Clamp01(1.0 - clamped);
    }

    private static double GetRepeatPenalty(AppSettings settings)
    {
        double penalty = settings.RepeatUrgencyPenalty;
        if (UtilityMethods.IsInvalid(penalty))
        {
            penalty = DefaultRepeatPenalty;
        }

        return Math.Clamp(penalty, 0.0, MaxRepeatPenalty);
    }

    private static double GetSizeBiasStrength(AppSettings settings)
    {
        double strength = settings.SizeBiasStrength;
        if (UtilityMethods.IsInvalid(strength))
        {
            strength = DefaultSizeBiasStrength;
        }

        return Math.Clamp(strength, 0.0, MaxSizeBiasStrength);
    }
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
