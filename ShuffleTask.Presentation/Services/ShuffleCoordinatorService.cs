using System;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using ShuffleTask.Models;
using ShuffleTask.ViewModels;

namespace ShuffleTask.Services;

public class ShuffleCoordinatorService : IDisposable
{
    private readonly IStorageService _storage;
    private readonly ISchedulerService _scheduler;
    private readonly INotificationService _notifications;
    private readonly TimeProvider _clock;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _initLock = new();
    private Task? _initializationTask;
    private CancellationTokenSource? _timerCts;
    private WeakReference<DashboardViewModel>? _dashboardRef;
    private bool _isPaused;
    private bool _disposed;

    public ShuffleCoordinatorService(IStorageService storage, ISchedulerService scheduler, INotificationService notifications, TimeProvider clock)
    {
        _storage = storage;
        _scheduler = scheduler;
        _notifications = notifications;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            CancelTimerInternal();
            _gate.Dispose();
        }

        _disposed = true;
    }

    public void RegisterDashboard(DashboardViewModel dashboard)
    {
        _dashboardRef = new WeakReference<DashboardViewModel>(dashboard);
    }

    public Task StartAsync() => ResumeInternalAsync();

    public Task ResumeAsync() => ResumeInternalAsync();

    private async Task ResumeInternalAsync()
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _isPaused = false;
        }
        finally
        {
            _gate.Release();
        }

        await ScheduleNextShuffleAsync().ConfigureAwait(false);
    }

    public async Task PauseAsync()
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _isPaused = true;
            CancelTimerInternal();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RefreshAsync()
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            CancelTimerInternal();
        }
        finally
        {
            _gate.Release();
        }

        if (!_isPaused)
        {
            await ScheduleNextShuffleAsync().ConfigureAwait(false);
        }
    }

    private Task EnsureInitializedAsync()
    {
        lock (_initLock)
        {
            _initializationTask ??= InitializeAsync();
            return _initializationTask;
        }
    }

    private async Task InitializeAsync()
    {
        await _storage.InitializeAsync().ConfigureAwait(false);
        await _notifications.InitializeAsync().ConfigureAwait(false);
    }

    private async Task ScheduleNextShuffleAsync()
    {
        if (_isPaused)
        {
            return;
        }

        await EnsureInitializedAsync().ConfigureAwait(false);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isPaused)
            {
                return;
            }

            await ScheduleNextShuffleUnsafeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ScheduleNextShuffleUnsafeAsync()
    {
        CancelTimerInternal();

        var settings = await _storage.GetSettingsAsync().ConfigureAwait(false);
        if (!ShouldAutoShuffle(settings))
        {
            ClearPendingShuffle();
            return;
        }

        DateTimeOffset now = GetCurrentInstant();
        ResetDailyCountIfNeeded(now);

        if (TryScheduleAfterDailyLimit(settings, now))
        {
            return;
        }

        if (await TryResumePendingShuffleAsync(settings, now).ConfigureAwait(false))
        {
            return;
        }

        await ScheduleFromAvailableTasksAsync(settings, now).ConfigureAwait(false);
    }

    private bool TryScheduleAfterDailyLimit(AppSettings settings, DateTimeOffset now)
    {
        if (!HasReachedDailyLimit(settings, now))
        {
            return false;
        }

        DateTimeOffset resumeAt = EnsureAllowed(GetNextDayStart(now, settings), settings);
        StartTimer(resumeAt, null);
        return true;
    }

    private async Task<bool> TryResumePendingShuffleAsync(AppSettings settings, DateTimeOffset now)
    {
        var pending = LoadPendingShuffle();
        if (!pending.NextAt.HasValue)
        {
            return false;
        }

        DateTimeOffset nextAt = pending.NextAt.Value;
        if (nextAt <= now)
        {
            nextAt = now;
        }

        if (string.IsNullOrEmpty(pending.TaskId))
        {
            StartTimer(nextAt, null);
            return true;
        }

        var pendingTask = await _storage.GetTaskAsync(pending.TaskId).ConfigureAwait(false);
        if (pendingTask != null && IsTaskValid(pendingTask, settings, nextAt))
        {
            StartTimer(nextAt, pending.TaskId);
            return true;
        }

        return false;
    }

    private async Task ScheduleFromAvailableTasksAsync(AppSettings settings, DateTimeOffset now)
    {
        var tasks = await _storage.GetTasksAsync().ConfigureAwait(false);
        if (tasks.Count == 0)
        {
            ClearPendingShuffle();
            var retryAtEmpty = EnsureAllowed(now.AddMinutes(30), settings);
            StartTimer(retryAtEmpty, null);
            return;
        }

        DateTimeOffset target = ComputeNextTarget(now, settings);
        var candidate = _scheduler.PickNextTask(tasks, settings, target);
        if (candidate == null)
        {
            DateTimeOffset retryAt = EnsureAllowed(now.AddMinutes(Math.Max(5, settings.MinGapMinutes)), settings);
            StartTimer(retryAt, null);
            return;
        }

        StartTimer(target, candidate.Id);
    }

    private DateTimeOffset ComputeNextTarget(DateTimeOffset now, AppSettings settings)
    {
        TimeSpan gap = _scheduler.NextGap(settings, now);
        DateTimeOffset target = now + gap;
        if (target <= now)
        {
            target = now.AddMinutes(1);
        }

        target = EnsureAllowed(target, settings);
        if (target <= now)
        {
            target = now.AddMinutes(1);
        }

        return target;
    }

    private void StartTimer(DateTimeOffset scheduledAt, string? taskId)
    {
        CancelTimerInternal();
        PersistPendingShuffle(taskId, scheduledAt);

        var cts = new CancellationTokenSource();
        _timerCts = cts;

        TimeSpan delay = scheduledAt - GetCurrentInstant();
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                if (cts.Token.IsCancellationRequested)
                {
                    Debug.WriteLine("ShuffleCoordinatorService timer cancelled");
                    return;
                }

                if (string.IsNullOrEmpty(taskId))
                {
                    await OnTimerReevaluateAsync(cts).ConfigureAwait(false);
                }
                else
                {
                    await ExecuteShuffleAsync(taskId, cts).ConfigureAwait(false);
                }
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("ShuffleCoordinatorService timer task canceled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ShuffleCoordinatorService timer error: {ex}");
            }
        });
    }

    private async Task OnTimerReevaluateAsync(CancellationTokenSource cts)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(_timerCts, cts))
            {
                _timerCts = null;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (_isPaused)
        {
            return;
        }

        await ScheduleNextShuffleAsync().ConfigureAwait(false);
    }

    private async Task ExecuteShuffleAsync(string taskId, CancellationTokenSource cts)
    {
        bool executed = false;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            executed = await ExecuteShuffleUnsafeAsync(taskId, cts).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        if (executed && !_isPaused)
        {
            await ScheduleNextShuffleAsync().ConfigureAwait(false);
        }
    }

    private async Task<bool> ExecuteShuffleUnsafeAsync(string taskId, CancellationTokenSource cts)
    {
        if (ReferenceEquals(_timerCts, cts))
        {
            _timerCts = null;
        }

        if (_isPaused)
        {
            return false;
        }

        var settings = await _storage.GetSettingsAsync().ConfigureAwait(false);
        if (!ShouldAutoShuffle(settings))
        {
            ClearPendingShuffle();
            return false;
        }

        DateTimeOffset now = GetCurrentInstant();
        ResetDailyCountIfNeeded(now);

        if (HandleQuietHoursOrLimit(settings, now, taskId))
        {
            return false;
        }

        TaskItem? task = await ResolveTaskAsync(taskId, settings, now).ConfigureAwait(false);
        if (task == null)
        {
            return false;
        }

        ClearPendingShuffle();
        PersistActiveTask(task, settings);
        await NotifyAsync(task, settings).ConfigureAwait(false);
        IncrementDailyCount(now);
        return true;
    }

    private bool HandleQuietHoursOrLimit(AppSettings settings, DateTimeOffset now, string taskId)
    {
        if (IsWithinQuietHours(now, settings))
        {
            DateTimeOffset resumeAt = EnsureAllowed(now, settings);
            StartTimer(resumeAt, taskId);
            return true;
        }

        if (HasReachedDailyLimit(settings, now))
        {
            DateTimeOffset resumeAt = EnsureAllowed(GetNextDayStart(now, settings), settings);
            StartTimer(resumeAt, null);
            return true;
        }

        return false;
    }

    private async Task<TaskItem?> ResolveTaskAsync(string taskId, AppSettings settings, DateTimeOffset now)
    {
        TaskItem? task = await _storage.GetTaskAsync(taskId).ConfigureAwait(false);
        if (task != null && IsTaskValid(task, settings, now))
        {
            return task;
        }

        var tasks = await _storage.GetTasksAsync().ConfigureAwait(false);
        TaskItem? candidate = _scheduler.PickNextTask(tasks, settings, now);
        if (candidate != null)
        {
            return candidate;
        }

        ClearPendingShuffle();
        DateTimeOffset retryAt = EnsureAllowed(now.AddMinutes(30), settings);
        StartTimer(retryAt, null);
        return null;
    }

    private async Task NotifyAsync(TaskItem task, AppSettings settings)
    {
        if (_dashboardRef != null && _dashboardRef.TryGetTarget(out var dashboard))
        {
            Task applyTask = MainThread.InvokeOnMainThreadAsync(() => dashboard.ApplyAutoShuffleAsync(task, settings));
            await applyTask.ConfigureAwait(false);
        }

        await _notifications.NotifyTaskAsync(task, Math.Max(1, settings.ReminderMinutes), settings).ConfigureAwait(false);
    }

    private static bool ShouldAutoShuffle(AppSettings settings)
    {
        if (settings is null)
        {
            return false;
        }

        if (!settings.Active)
        {
            return false;
        }

        if (!settings.AutoShuffleEnabled)
        {
            return false;
        }

        if (!settings.EnableNotifications)
        {
            return false;
        }

        return true;
    }

    private static bool HasReachedDailyLimit(AppSettings settings, DateTimeOffset now)
    {
        int max = settings.MaxDailyShuffles;
        if (max <= 0)
        {
            return false;
        }

        var (date, count) = LoadDailyCount();
        if (!date.HasValue)
        {
            return false;
        }

        DateTimeOffset local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
        return date.Value.Date == local.Date && count >= max;
    }

    private static DateTimeOffset GetNextDayStart(DateTimeOffset now, AppSettings settings)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
        DateTime nextDay = local.Date.AddDays(1);
        if (settings.QuietHoursStart != settings.QuietHoursEnd)
        {
            DateTime localCandidate = nextDay + settings.QuietHoursEnd;
            return new DateTimeOffset(localCandidate, local.Offset).ToOffset(TimeSpan.Zero);
        }

        DateTime defaultCandidate = nextDay + settings.WorkStart;
        return new DateTimeOffset(defaultCandidate, local.Offset).ToOffset(TimeSpan.Zero);
    }

    private static bool IsWithinQuietHours(DateTimeOffset time, AppSettings settings)
    {
        var start = settings.QuietHoursStart;
        var end = settings.QuietHoursEnd;

        if (start == end)
        {
            return false;
        }

        DateTimeOffset local = TimeZoneInfo.ConvertTime(time, TimeZoneInfo.Local);
        TimeSpan t = local.TimeOfDay;
        if (start < end)
        {
            return t >= start && t < end;
        }

        return t >= start || t < end;
    }

    private static DateTimeOffset EnsureAllowed(DateTimeOffset candidate, AppSettings settings)
    {
        if (!IsWithinQuietHours(candidate, settings))
        {
            return candidate;
        }

        return GetQuietHoursEnd(candidate, settings);
    }

    private static DateTimeOffset GetQuietHoursEnd(DateTimeOffset current, AppSettings settings)
    {
        var start = settings.QuietHoursStart;
        var end = settings.QuietHoursEnd;
        DateTimeOffset local = TimeZoneInfo.ConvertTime(current, TimeZoneInfo.Local);
        DateTime baseDate = local.Date;

        if (start < end)
        {
            DateTime quietEnd = baseDate + end;
            if (local.TimeOfDay < end)
            {
                return new DateTimeOffset(quietEnd, local.Offset).ToOffset(TimeSpan.Zero);
            }

            DateTime next = quietEnd.AddDays(1);
            return new DateTimeOffset(next, local.Offset).ToOffset(TimeSpan.Zero);
        }
        else
        {
            if (local.TimeOfDay >= start)
            {
                DateTime next = baseDate.AddDays(1) + end;
                return new DateTimeOffset(next, local.Offset).ToOffset(TimeSpan.Zero);
            }

            if (local.TimeOfDay < end)
            {
                DateTime sameDay = baseDate + end;
                return new DateTimeOffset(sameDay, local.Offset).ToOffset(TimeSpan.Zero);
            }

            DateTime fallback = baseDate + start;
            return new DateTimeOffset(fallback, local.Offset).ToOffset(TimeSpan.Zero);
        }
    }

    private static bool IsTaskValid(TaskItem task, AppSettings settings, DateTimeOffset when)
    {
        if (task.Paused)
        {
            return false;
        }

        return TimeWindowService.AllowedNow(task.AllowedPeriod, when, settings);
    }

    private static void PersistPendingShuffle(string? taskId, DateTimeOffset scheduledAt)
    {
        Preferences.Default.Set(PreferenceKeys.NextShuffleAt, scheduledAt.ToString("O", CultureInfo.InvariantCulture));
        if (string.IsNullOrEmpty(taskId))
        {
            Preferences.Default.Remove(PreferenceKeys.PendingShuffleTaskId);
        }
        else
        {
            Preferences.Default.Set(PreferenceKeys.PendingShuffleTaskId, taskId);
        }
    }

    private static void PersistActiveTask(TaskItem task, AppSettings settings)
    {
        int seconds = Math.Max(1, settings.ReminderMinutes) * 60;
        Preferences.Default.Set(PreferenceKeys.CurrentTaskId, task.Id);
        Preferences.Default.Set(PreferenceKeys.RemainingSeconds, seconds);
    }

    private static (DateTimeOffset? NextAt, string TaskId) LoadPendingShuffle()
    {
        string iso = Preferences.Default.Get(PreferenceKeys.NextShuffleAt, string.Empty);
        string taskId = Preferences.Default.Get(PreferenceKeys.PendingShuffleTaskId, string.Empty);

        if (!string.IsNullOrWhiteSpace(iso) && DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var nextAt))
        {
            return (nextAt, taskId);
        }

        return (null, taskId);
    }

    private static (DateTimeOffset? Date, int Count) LoadDailyCount()
    {
        string iso = Preferences.Default.Get(PreferenceKeys.ShuffleCountDate, string.Empty);
        int count = Preferences.Default.Get(PreferenceKeys.ShuffleCount, 0);

        if (!string.IsNullOrWhiteSpace(iso) && DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
        {
            return (date, count);
        }

        return (null, 0);
    }

    private static void ResetDailyCountIfNeeded(DateTimeOffset now)
    {
        string iso = Preferences.Default.Get(PreferenceKeys.ShuffleCountDate, string.Empty);
        DateTimeOffset local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
        bool needsReset = true;
        if (!string.IsNullOrWhiteSpace(iso) && DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
        {
            DateTimeOffset existingLocal = TimeZoneInfo.ConvertTime(date, TimeZoneInfo.Local);
            needsReset = existingLocal.Date != local.Date;
        }

        if (needsReset)
        {
            var storedLocal = new DateTimeOffset(local.Date, local.Offset);
            Preferences.Default.Set(PreferenceKeys.ShuffleCountDate, storedLocal.ToString("O", CultureInfo.InvariantCulture));
            Preferences.Default.Set(PreferenceKeys.ShuffleCount, 0);
        }
    }

    private static void IncrementDailyCount(DateTimeOffset now)
    {
        var (date, count) = LoadDailyCount();
        DateTimeOffset local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
        if (!date.HasValue || TimeZoneInfo.ConvertTime(date.Value, TimeZoneInfo.Local).Date != local.Date)
        {
            date = new DateTimeOffset(local.Date, local.Offset);
            count = 0;
        }

        Preferences.Default.Set(PreferenceKeys.ShuffleCountDate, date.Value.ToString("O", CultureInfo.InvariantCulture));
        Preferences.Default.Set(PreferenceKeys.ShuffleCount, count + 1);
    }

    private static void ClearPendingShuffle()
    {
        Preferences.Default.Remove(PreferenceKeys.NextShuffleAt);
        Preferences.Default.Remove(PreferenceKeys.PendingShuffleTaskId);
    }

    private void CancelTimerInternal()
    {
        var existing = Interlocked.Exchange(ref _timerCts, null);
        if (existing != null)
        {
            try
            {
                existing.Cancel();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ShuffleCoordinatorService cancellation error: {ex}");
            }
            finally
            {
                existing.Dispose();
            }
        }
    }

    private DateTimeOffset GetCurrentInstant()
        => _clock.GetUtcNow();
}
