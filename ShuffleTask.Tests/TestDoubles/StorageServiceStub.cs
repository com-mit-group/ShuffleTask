using ShuffleTask.Models;
using ShuffleTask.Services;

namespace ShuffleTask.Tests.TestDoubles;

public class StorageServiceStub : IStorageService
{
    private readonly Dictionary<string, TaskItem> _tasks = new();
    private bool _initialized;
    private AppSettings _settings = new();

    public int InitializeCallCount { get; private set; }
    public int GetTasksCallCount { get; private set; }
    public int UpdateTaskCallCount { get; private set; }
    public int DeleteTaskCallCount { get; private set; }
    public int MarkDoneCallCount { get; private set; }
    public int SnoozeCallCount { get; private set; }
    public int ResumeCallCount { get; private set; }
    public int GetSettingsCallCount { get; private set; }
    public int SetSettingsCallCount { get; private set; }

    public Task InitializeAsync()
    {
        InitializeCallCount++;
        _initialized = true;
        return Task.CompletedTask;
    }

    public Task<List<TaskItem>> GetTasksAsync()
    {
        EnsureInitialized();
        AutoResumeDueTasks();
        GetTasksCallCount++;
        var snapshot = _tasks.Values
            .OrderByDescending(t => t.CreatedAt)
            .Select(Clone)
            .ToList();
        return Task.FromResult(snapshot);
    }

    public Task<TaskItem?> GetTaskAsync(string id)
    {
        EnsureInitialized();
        AutoResumeDueTasks();
        return Task.FromResult(_tasks.TryGetValue(id, out var item) ? Clone(item) : null);
    }

    public Task AddTaskAsync(TaskItem item)
    {
        EnsureInitialized();
        _tasks[item.Id] = Clone(item);
        return Task.CompletedTask;
    }

    public Task UpdateTaskAsync(TaskItem item)
    {
        EnsureInitialized();
        UpdateTaskCallCount++;
        _tasks[item.Id] = Clone(item);
        return Task.CompletedTask;
    }

    public Task DeleteTaskAsync(string id)
    {
        EnsureInitialized();
        DeleteTaskCallCount++;
        _tasks.Remove(id);
        return Task.CompletedTask;
    }

    public Task<TaskItem?> MarkTaskDoneAsync(string id)
    {
        EnsureInitialized();
        MarkDoneCallCount++;

        if (!_tasks.TryGetValue(id, out var existing))
        {
            return Task.FromResult<TaskItem?>(null);
        }

        DateTime nowUtc = DateTime.UtcNow;
        DateTime doneAt = EnsureUtc(nowUtc);

        existing.LastDoneAt = doneAt;
        existing.CompletedAt = doneAt;
        existing.Status = TaskLifecycleStatus.Completed;
        existing.SnoozedUntil = null;
        existing.NextEligibleAt = ComputeNextEligibleUtc(existing, nowUtc);

        _tasks[id] = Clone(existing);
        return Task.FromResult<TaskItem?>(Clone(existing));
    }

    public Task<TaskItem?> SnoozeTaskAsync(string id, TimeSpan duration)
    {
        EnsureInitialized();
        SnoozeCallCount++;

        if (duration <= TimeSpan.Zero)
        {
            duration = TimeSpan.FromMinutes(15);
        }

        if (!_tasks.TryGetValue(id, out var existing))
        {
            return Task.FromResult<TaskItem?>(null);
        }

        DateTime nowUtc = DateTime.UtcNow;
        DateTime until = EnsureUtc(nowUtc.Add(duration));

        existing.Status = TaskLifecycleStatus.Snoozed;
        existing.SnoozedUntil = until;
        existing.NextEligibleAt = until;
        existing.CompletedAt = null;

        _tasks[id] = Clone(existing);
        return Task.FromResult<TaskItem?>(Clone(existing));
    }

    public Task<TaskItem?> ResumeTaskAsync(string id)
    {
        EnsureInitialized();
        ResumeCallCount++;

        if (!_tasks.TryGetValue(id, out var existing))
        {
            return Task.FromResult<TaskItem?>(null);
        }

        ApplyResume(existing);
        _tasks[id] = Clone(existing);
        return Task.FromResult<TaskItem?>(Clone(existing));
    }

    public Task<AppSettings> GetSettingsAsync()
    {
        EnsureInitialized();
        GetSettingsCallCount++;
        return Task.FromResult(Clone(_settings));
    }

    public Task SetSettingsAsync(AppSettings settings)
    {
        EnsureInitialized();
        SetSettingsCallCount++;
        _settings = Clone(settings);
        return Task.CompletedTask;
    }

    private void AutoResumeDueTasks()
    {
        DateTime nowUtc = DateTime.UtcNow;

        foreach (var task in _tasks.Values)
        {
            if (task.Status == TaskLifecycleStatus.Active)
            {
                continue;
            }

            if (task.NextEligibleAt == null)
            {
                continue;
            }

            DateTime nextUtc = EnsureUtc(task.NextEligibleAt.Value);
            if (nextUtc <= nowUtc)
            {
                ApplyResume(task);
            }
        }
    }

    private static void ApplyResume(TaskItem task)
    {
        task.Status = TaskLifecycleStatus.Active;
        task.SnoozedUntil = null;
        task.NextEligibleAt = null;
        task.CompletedAt = null;
    }

    private static DateTime? ComputeNextEligibleUtc(TaskItem task, DateTime nowUtc)
    {
        return task.Repeat switch
        {
            RepeatType.None => null,
            RepeatType.Daily => EnsureUtc(nowUtc.ToLocalTime().AddDays(1)),
            RepeatType.Weekly => ComputeWeeklyNext(task.Weekdays, nowUtc),
            RepeatType.Interval => EnsureUtc(nowUtc.ToLocalTime().AddDays(Math.Max(1, task.IntervalDays))),
            _ => null
        };
    }

    private static DateTime? ComputeWeeklyNext(Weekdays weekdays, DateTime nowUtc)
    {
        DateTime local = nowUtc.ToLocalTime();
        if (weekdays == Weekdays.None)
        {
            weekdays = DayToWeekdayFlag(local.DayOfWeek);
        }

        for (int offset = 1; offset <= 7; offset++)
        {
            DateTime candidate = DateTime.SpecifyKind(local.Date.AddDays(offset).Add(local.TimeOfDay), DateTimeKind.Local);
            Weekdays flag = DayToWeekdayFlag(candidate.DayOfWeek);
            if ((weekdays & flag) != 0)
            {
                return EnsureUtc(candidate);
            }
        }

        DateTime fallback = DateTime.SpecifyKind(local.Date.AddDays(7).Add(local.TimeOfDay), DateTimeKind.Local);
        return EnsureUtc(fallback);
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

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("StorageService not initialized. Call InitializeAsync() first.");
        }
    }

    private static TaskItem Clone(TaskItem task) => TaskItem.Clone(task);

    private static AppSettings Clone(AppSettings settings)
    {
        return new AppSettings
        {
            WorkStart = settings.WorkStart,
            WorkEnd = settings.WorkEnd,
            MinGapMinutes = settings.MinGapMinutes,
            MaxGapMinutes = settings.MaxGapMinutes,
            ReminderMinutes = settings.ReminderMinutes,
            EnableNotifications = settings.EnableNotifications,
            SoundOn = settings.SoundOn,
            Active = settings.Active,
            StreakBias = settings.StreakBias,
            StableRandomnessPerDay = settings.StableRandomnessPerDay,
            ImportanceWeight = settings.ImportanceWeight,
            UrgencyWeight = settings.UrgencyWeight,
            UrgencyDeadlineShare = settings.UrgencyDeadlineShare,
            RepeatUrgencyPenalty = settings.RepeatUrgencyPenalty,
            SizeBiasStrength = settings.SizeBiasStrength
        };
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
}
