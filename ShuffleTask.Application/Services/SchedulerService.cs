using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Utilities;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Services;

public class SchedulerService : ISchedulerService
{
    private readonly bool _deterministic;
    private readonly IShuffleLogger? _logger;

    public SchedulerService(bool deterministic = false, IShuffleLogger? logger = null)
    {
        _deterministic = deterministic;
        _logger = logger;
    }

    public TimeSpan NextGap(AppSettings settings, DateTimeOffset now)
    {
        int min = Math.Max(0, settings.MinGapMinutes);
        int max = Math.Max(min, settings.MaxGapMinutes);

        if (_deterministic)
        {
            // Deterministic: return midpoint
            int mid = (min + max) / 2;
            return TimeSpan.FromMinutes(mid);
        }

        // Pick RNG based on settings
        Random rng = UtilityMethods.CreateRng(settings, now, null);

        int range = Math.Max(1, (max - min + 1));
        int minutes = min + rng.Next(0, range);
        return TimeSpan.FromMinutes(minutes);
    }

    public TaskItem? PickNextTask(IEnumerable<TaskItem> tasks, AppSettings settings, DateTimeOffset now)
        => PickNextTask(tasks, settings, now, _deterministic, _logger);

    public static TaskItem? PickNextTask(IEnumerable<TaskItem> tasks, AppSettings settings, DateTimeOffset now, bool deterministic, IShuffleLogger? logger = null)
    {
        if (tasks == null)
        {
            return null;
        }

        var candidates = tasks
            .Where(task => task is not null)
            .Where(task => UtilityMethods.LifecycleEligible(task, now.UtcDateTime))
            .Where(task => !task.Paused)
            .Where(task => TimeWindowService.AllowedNow(task.AllowedPeriod, now, settings))
            .ToList();

        if (candidates.Count == 0)
        {
            logger?.LogTaskSelection("", "", "No eligible candidates", 0, TimeSpan.Zero);
            return null;
        }

        // Check for cut-in-line tasks first
        var cutInLineTask = candidates
            .FirstOrDefault(task => task.CutInLineMode != CutInLineMode.None);
        
        if (cutInLineTask != null)
        {
            logger?.LogTaskSelection(cutInLineTask.Id, cutInLineTask.Title, "Task selected via cut-in-line priority", candidates.Count, TimeSpan.Zero);
            return cutInLineTask;
        }

        List<ScoredTask> scored = ComputeScores(settings, now, deterministic, candidates);
        var selected = GetBestScoredTask(settings, now, deterministic, scored);
        
        if (selected != null)
        {
            logger?.LogTaskSelection(selected.Id, selected.Title, "Task selected by scoring", candidates.Count, TimeSpan.Zero);
        }
        
        return selected;
    }

    private static List<ScoredTask> ComputeScores(AppSettings settings, DateTimeOffset now, bool deterministic, List<TaskItem> candidates)
    {
        List<ScoredTask> scored = [];

        foreach (TaskItem task in candidates)
        {
            ImportanceUrgencyScore components = ImportanceUrgencyCalculator.Calculate(task, now, settings);
            double score = components.CombinedScore;

            if (!deterministic)
            {
                // jitter in [-0.5, 0.5]
                Random rng = UtilityMethods.CreateRng(settings, now, task);
                score += rng.NextDouble() - 0.5;
            }

            scored.Add(new ScoredTask(task, score));
        }

        return scored;
    }

    private static TaskItem GetBestScoredTask(AppSettings settings, DateTimeOffset now, bool deterministic, List<ScoredTask> scored)
    {
        if (deterministic)
        {
            return UtilityMethods.DeterministicMaxScoredTask(scored);
        }

        // Softmax sampling
        double[] expScores = UtilityMethods.ExponentArray(scored);
        double expSum = expScores.Sum();
        if (expSum <= 0)
        {
            return UtilityMethods.DeterministicMaxScoredTask(scored);
        }

        double[] cumulative = new double[expScores.Length];
        double acc = 0;
        for (int i = 0; i < expScores.Length; i++)
        {
            acc += expScores[i];
            cumulative[i] = acc;
        }

        double sample = settings.StableRandomnessPerDay
            ? UtilityMethods.NextStableSample(now)
            : Random.Shared.NextDouble();

        double r = sample * expSum;

        for (int i = 0; i < cumulative.Length; i++)
        {
            if (r <= cumulative[i])
            {
                return scored[i].Task;
            }
        }

        return scored[^1].Task;
    }
}
