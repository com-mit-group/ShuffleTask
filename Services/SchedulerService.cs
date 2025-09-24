using ShuffleTask.Models;

namespace ShuffleTask.Services;

public class SchedulerService
{
    private readonly bool _deterministic;

    private static readonly object _stableSampleLock = new();
    private static int _stableSampleSeed = int.MinValue;
    private static int _stableSampleIndex;

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
            var components = ImportanceUrgencyCalculator.Calculate(t, nowLocal, s);
            double score = components.CombinedScore;

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

        double sample;
        if (s.StableRandomnessPerDay)
        {
            int daySeed = nowLocal.Year * 10000 + nowLocal.Month * 100 + nowLocal.Day;
            sample = NextStableSample(daySeed);
        }
        else
        {
            sample = Random.Shared.NextDouble();
        }

        double r = sample * sum;

        for (int i = 0; i < cumulative.Length; i++)
        {
            if (r <= cumulative[i])
                return scored[i].Task;
        }

        return scored.Last().Task;
    }

    private static double NextStableSample(int daySeed)
    {
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
}
