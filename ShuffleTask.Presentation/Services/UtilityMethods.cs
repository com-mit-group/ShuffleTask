using ShuffleTask.Models;

namespace ShuffleTask.Services;

internal static class UtilityMethods
{

    private static readonly object _stableSampleLock = new();
    private static int _stableSampleSeed = int.MinValue;
    private static int _stableSampleIndex;

    public static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);

    public static Random CreateRng(AppSettings settings, DateTimeOffset now, TaskItem? task)
    {
        Random rng;
        if (settings.StableRandomnessPerDay)
        {
            int seed = ComputeStableTaskSeed(now, task);
            rng = new Random(seed);
        }
        else
        {
            rng = new Random();
        }

        return rng;
    }

    public static TaskItem DeterministicMaxScoredTask(List<ScoredTask> scored)
    {
        return DescendingScores(scored).First().Task;
    }

    public static DateTimeOffset? EnsureUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        DateTime dt = value.Value;
        return dt.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(dt, TimeSpan.Zero),
            DateTimeKind.Local => new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero),
            _ => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero)
        };
    }

    public static double[] ExponentArray(List<ScoredTask> scored)
    {
        double maxScore = scored.Max(x => x.Score);
        double[] expScores = scored.Select(x => Math.Exp(x.Score - maxScore)).ToArray();
        return expScores;
    }

    public static bool IsInvalid(double value) => double.IsNaN(value) || double.IsInfinity(value);

    public static bool LifecycleEligible(TaskItem task, DateTime nowUtc)
    {
        if (task.Status == TaskLifecycleStatus.Active)
        {
            return true;
        }

        if (task.Status == TaskLifecycleStatus.Snoozed || task.Status == TaskLifecycleStatus.Completed)
        {
            if (!task.NextEligibleAt.HasValue)
            {
                return false;
            }

            DateTime eligibleUtc = EnsureUtc(task.NextEligibleAt.Value);
            return eligibleUtc <= nowUtc;
        }

        return true;
    }

    public static double NextStableSample(DateTimeOffset now)
    {
        int daySeed = GetDaySeed(now);
        lock (_stableSampleLock)
        {
            if (_stableSampleSeed != daySeed)
            {
                _stableSampleSeed = daySeed;
                _stableSampleIndex = 0;
            }

            int combined = unchecked(daySeed * 397) ^ _stableSampleIndex++;
            combined &= int.MaxValue;

            var rng = new Random(combined);
            return rng.NextDouble();
        }
    }

    private static int ComputeStableTaskSeed(DateTimeOffset now, TaskItem? task)
    {
        int daySeed = GetDaySeed(now);
        if (task is not null)
        {
            unchecked { daySeed *= 31 + task.Id.GetHashCode(); }
        }
        else
        {
            daySeed ^= 0x5f3759df;
        }

        return daySeed;
    }

    private static IOrderedEnumerable<ScoredTask> DescendingScores(List<ScoredTask> scored)
    {
        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Task.Id, StringComparer.Ordinal);
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static int GetDaySeed(DateTimeOffset now)
    {
        DateTimeOffset local = LocalizeTime(now);
        return local.Year * 10000 + local.Month * 100 + local.Day;
    }

    private static DateTimeOffset LocalizeTime(DateTimeOffset now)
    {
        return TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
    }
}