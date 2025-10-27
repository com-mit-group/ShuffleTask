using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Services;
using ShuffleTask.Application.Utilities;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Domain.Events;
using ShuffleTask.Presentation.Services;
using ShuffleTask.Presentation.Utilities;
using Yaref92.Events.Abstractions;

namespace ShuffleTask.ViewModels;

public partial class DashboardViewModel : ObservableObject,
    IAsyncEventSubscriber<ShuffleStateChanged>
{
    private readonly IStorageService _storage;
    private readonly ISchedulerService _scheduler;
    private readonly INotificationService _notifications;
    private readonly ShuffleCoordinatorService _coordinator;
    private readonly TimeProvider _clock;
    private readonly IRealtimeSyncService? _sync;

    private TaskItem? _activeTask;
    private AppSettings? _settings;
    private PomodoroSession? _pomodoroSession;
    private TimerRequest? _currentTimer;
    private bool _isInitialized;

    private bool CanBroadcast => _sync?.ShouldBroadcastLocalChanges ?? false;

    private const string DefaultTitle = "Shuffle a task";
    private const string DefaultDescription = "Tap Shuffle to pick what comes next.";
    private const string DefaultSchedule = "No schedule yet.";

    public DashboardViewModel(
        IStorageService storage,
        ISchedulerService scheduler,
        INotificationService notifications,
        ShuffleCoordinatorService coordinator,
        TimeProvider clock,
        IEventAggregator aggregator,
        IRealtimeSyncService? syncService = null)
    {
        _storage = storage;
        _scheduler = scheduler;
        _notifications = notifications;
        _coordinator = coordinator;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _sync = syncService;
        ArgumentNullException.ThrowIfNull(aggregator);

        Title = DefaultTitle;
        Description = DefaultDescription;
        Schedule = DefaultSchedule;
        TimerText = "--:--";
        CycleStatus = string.Empty;
        PhaseBadge = string.Empty;

        aggregator.SubscribeToEventType<ShuffleStateChanged>(this);
    }

    public Task OnNextAsync(ShuffleStateChanged @event, CancellationToken cancellationToken = default)
        => @event == null
            ? Task.CompletedTask
            : MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await HandleSyncStateAsync(@event).ConfigureAwait(false);
            });

    public event EventHandler<TimerRequest>? CountdownRequested;
    public event EventHandler? CountdownCleared;

    public enum PomodoroPhase
    {
        Focus,
        Break
    }

    public sealed record TimerRequest(
        TimeSpan Duration,
        TimerMode Mode,
        PomodoroPhase? Phase,
        int CycleIndex,
        int CycleCount,
        int FocusMinutes,
        int BreakMinutes)
    {
        public static TimerRequest Pomodoro(TimeSpan duration, PomodoroPhase phase, int cycleIndex, int cycleCount, int focusMinutes, int breakMinutes)
            => new(duration, TimerMode.Pomodoro, phase, cycleIndex, cycleCount, focusMinutes, breakMinutes);

        public static TimerRequest PomodoroFromMinutes(PomodoroPhase phase, int cycleIndex, int cycleCount, int focusMinutes, int breakMinutes)
        {
            int safeFocus = Math.Max(1, focusMinutes);
            int safeBreak = Math.Max(1, breakMinutes);
            var duration = phase == PomodoroPhase.Break
                ? TimeSpan.FromMinutes(safeBreak)
                : TimeSpan.FromMinutes(safeFocus);

            return Pomodoro(duration, phase, cycleIndex, cycleCount, safeFocus, safeBreak);
        }

        public static TimerRequest LongInterval(TimeSpan duration)
            => new(duration, TimerMode.LongInterval, null, 0, 0, 0, 0);

        public static TimerRequest LongIntervalFromMinutes(int minutes)
            => LongInterval(TimeSpan.FromMinutes(Math.Max(1, minutes)));
    }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private string _schedule;

    [ObservableProperty]
    private string _timerText;

    [ObservableProperty]
    private bool _hasTask;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _cycleStatus = string.Empty;

    [ObservableProperty]
    private string _phaseBadge = string.Empty;

    [ObservableProperty]
    private bool _isPomodoroVisible;

    public string? ActiveTaskId => _activeTask?.Id;

    public async Task InitializeAsync()
    {
        if (!_isInitialized)
        {
            await _storage.InitializeAsync();
            await _notifications.InitializeAsync();
            _coordinator.RegisterDashboard(this);
            _isInitialized = true;
        }

        await LoadSettingsAsync();
    }

    private async Task HandleSyncStateAsync(ShuffleStateChanged state)
    {
        if (state == null)
        {
            return;
        }

        await EnsureSettingsAsync();

        if (!state.HasActiveTask)
        {
            ShowClearedState();
            return;
        }

        var task = await _storage.GetTaskAsync(state.TaskId!).ConfigureAwait(false);
        if (task == null)
        {
            ShowClearedState();
            return;
        }

        BindTask(task);

        TimerRequest? timer = ApplyRemainingTime(state, ConfigureTimerFromState(state));

        if (timer != null)
        {
            CountdownRequested?.Invoke(this, timer);
        }
    }

    private void ShowClearedState()
    {
        ShowDefaultState();
        CountdownCleared?.Invoke(this, EventArgs.Empty);
    }

    private TimerRequest? ConfigureTimerFromState(ShuffleStateChanged state)
    {
        return state.TimerMode == (int)TimerMode.Pomodoro
            ? ConfigurePomodoroTimer(state)
            : ConfigureIntervalTimer(state);
    }

    private TimerRequest? ConfigurePomodoroTimer(ShuffleStateChanged state)
    {
        int focus = Math.Max(1, state.FocusMinutes ?? _settings?.FocusMinutes ?? 15);
        int breakMinutes = Math.Max(1, state.BreakMinutes ?? _settings?.BreakMinutes ?? 5);
        int cycles = Math.Max(1, state.PomodoroCycleCount ?? _settings?.PomodoroCycles ?? 1);
        int cycleIndex = Math.Max(1, state.PomodoroCycleIndex ?? 1);
        var phase = state.PomodoroPhase == 1 ? PomodoroPhase.Break : PomodoroPhase.Focus;
        TimeSpan duration = state.TimerDurationSeconds.HasValue
            ? TimeSpan.FromSeconds(Math.Max(1, state.TimerDurationSeconds.Value))
            : (phase == PomodoroPhase.Break ? TimeSpan.FromMinutes(breakMinutes) : TimeSpan.FromMinutes(focus));

        var request = TimerRequest.Pomodoro(duration, phase, cycleIndex, cycles, focus, breakMinutes);
        _pomodoroSession = PomodoroSession.FromState(request);
        _currentTimer = _pomodoroSession.CurrentRequest();
        UpdateIndicators(_currentTimer);
        return _currentTimer;
    }

    private TimerRequest ConfigureIntervalTimer(ShuffleStateChanged state)
    {
        TimeSpan duration = state.TimerDurationSeconds.HasValue
            ? TimeSpan.FromSeconds(Math.Max(1, state.TimerDurationSeconds.Value))
            : TimeSpan.FromMinutes(Math.Max(1, _settings?.ReminderMinutes ?? 60));

        var request = TimerRequest.LongInterval(duration);
        _pomodoroSession = null;
        _currentTimer = request;
        UpdateIndicators(request);
        return request;
    }

    private TimerRequest? ApplyRemainingTime(ShuffleStateChanged state, TimerRequest? fallback)
    {
        TimeSpan? remaining = null;
        if (state.TimerExpiresUtc.HasValue)
        {
            DateTime expiresUtc = state.TimerExpiresUtc.Value;
            var now = _clock.GetUtcNow();
            remaining = expiresUtc - now.UtcDateTime;
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }
        }

        var timerToStart = _currentTimer ?? request;
        if (remaining.HasValue)
        {
            TimerText = FormatTimerText(remaining.Value);
            if (timerToStart != null)
            {
                timerToStart = timerToStart with { Duration = remaining.Value };
                _currentTimer = timerToStart;
            }
        }
        else if (fallback != null)
        {
            TimerText = FormatTimerText(fallback.Duration);
        }

        return timerToStart;
    }

    private async Task EnsureSettingsAsync()
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
            return;
        }

        await LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        _settings = await _storage.GetSettingsAsync();
    }

    [RelayCommand]
    private Task ShuffleAsync() => ShuffleInternalAsync(allowRepeat: false);

    public Task ShuffleAfterTimeoutAsync() => ShuffleInternalAsync(allowRepeat: true);

    private async Task ShuffleInternalAsync(bool allowRepeat)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await EnsureSettingsAsync();
            var settings = _settings ?? throw new InvalidOperationException("Settings unavailable.");

            string trigger = allowRepeat ? "manual-timeout" : "manual-shuffle";

            if (!settings.Active)
            {
                ShowMessage("Scheduling paused", "Enable the scheduler from Settings to shuffle tasks.");
                return;
            }

            var tasks = await _storage.GetTasksAsync();
            DateTimeOffset now = _clock.GetUtcNow();
            string? previousId = _activeTask?.Id;

            var next = PickNextCandidate(tasks, settings, now, previousId);
            if (next == null)
            {
                ShowMessage("No tasks ready", "Add a task or adjust filters to get started.");
                return;
            }

            bool isSameTask = !string.IsNullOrEmpty(previousId) && next.Id == previousId;
            if (isSameTask && !allowRepeat)
            {
                return;
            }

            await CutInLineUtilities.ClearCutInLineOnceAsync(next, _storage);

            BindTask(next);

            var effectiveSettings = TaskTimerSettings.Resolve(next, settings);
            var (mode, reminderMinutes, focusMinutes, breakMinutes, pomodoroCycles) = effectiveSettings;
            string displayTitle = string.IsNullOrWhiteSpace(next.Title) ? "Untitled task" : next.Title;

            if (mode == TimerMode.Pomodoro)
            {
                _pomodoroSession = PomodoroSession.Create(focusMinutes, breakMinutes, pomodoroCycles);
                var request = _pomodoroSession.CurrentRequest();
                _currentTimer = request;
                UpdateIndicators(request);
                TimerText = FormatTimerText(request.Duration);
                CountdownRequested?.Invoke(this, request);

                if (settings.EnableNotifications)
                {
                    await _notifications.NotifyTaskAsync(next, _pomodoroSession.FocusMinutes, settings);
                }

                await BroadcastNotificationAsync(
                    "Reminder",
                    $"Time for: {displayTitle}\nYou have {_pomodoroSession.FocusMinutes} minutes.",
                    next.Id,
                    isReminder: true,
                    delay: TimeSpan.Zero);

                await SchedulePomodoroNotificationsAsync(next, _pomodoroSession, settings);

                await BroadcastActiveStateAsync(next, request, trigger, isAutoShuffle: false);
            }
            else
            {
                _pomodoroSession = null;
                int minutes = Math.Max(1, reminderMinutes);
                var request = TimerRequest.LongIntervalFromMinutes(minutes);
                _currentTimer = request;
                UpdateIndicators(request);
                TimerText = FormatTimerText(request.Duration);
                CountdownRequested?.Invoke(this, request);

                if (settings.EnableNotifications)
                {
                    await _notifications.NotifyTaskAsync(next, minutes, settings);
                }

                await BroadcastNotificationAsync(
                    "Reminder",
                    $"Time for: {displayTitle}\nYou have {minutes} minutes.",
                    next.Id,
                    isReminder: true,
                    delay: TimeSpan.Zero);

                await BroadcastActiveStateAsync(next, request, trigger, isAutoShuffle: false);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DoneAsync()
    {
        if (_activeTask == null)
        {
            return;
        }

        var updated = await _storage.MarkTaskDoneAsync(_activeTask.Id);
        if (updated != null)
        {
            _activeTask = updated;
        }

        var snapshot = _activeTask;
        ShowMessage("Task complete", "Shuffle another task when you're ready.");
        EmitTimerResetTelemetry("done", snapshot);
        await BroadcastClearedStateAsync("manual-done");
    }

    [RelayCommand]
    private async Task SnoozeAsync()
    {
        if (_activeTask == null)
        {
            return;
        }

        await EnsureSettingsAsync();
        var settings = _settings ?? new AppSettings();

        int snoozeMinutes = Math.Max(15, settings.MinGapMinutes);
        var duration = TimeSpan.FromMinutes(snoozeMinutes);

        var updated = await _storage.SnoozeTaskAsync(_activeTask.Id, duration);
        if (updated != null)
        {
            _activeTask = updated;
        }

        var snapshot = _activeTask;
        ShowMessage("Task snoozed", "Shuffle another task when you're ready.");
        EmitTimerResetTelemetry("snooze", snapshot);
        await BroadcastClearedStateAsync("manual-snooze");
    }

    public async Task<bool> RestoreTaskAsync(string? taskId, TimeSpan? remaining, TimerRequest? timerState)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            ShowDefaultState();
            return false;
        }

        await EnsureSettingsAsync();
        var task = await _storage.GetTaskAsync(taskId);
        if (task == null)
        {
            ShowDefaultState();
            return false;
        }

        BindTask(task);

        if (timerState?.Mode == TimerMode.Pomodoro && timerState.Phase.HasValue)
        {
            _pomodoroSession = PomodoroSession.FromState(timerState);
            _currentTimer = _pomodoroSession.CurrentRequest();
            UpdateIndicators(_currentTimer);
        }
        else if (timerState != null)
        {
            _pomodoroSession = null;
            _currentTimer = timerState;
            UpdateIndicators(timerState);
        }
        else
        {
            _pomodoroSession = null;
            UpdateIndicators(null);
        }

        if (remaining.HasValue)
        {
            TimerText = FormatTimerText(remaining.Value);
        }
        else if (_currentTimer != null)
        {
            TimerText = FormatTimerText(_currentTimer.Duration);
        }

        return true;
    }

    public async Task HandleCountdownCompletedAsync()
    {
        await EnsureSettingsAsync();
        if (_settings == null)
        {
            return;
        }

        if (_currentTimer?.Mode == TimerMode.Pomodoro && _pomodoroSession != null && _activeTask != null)
        {
            var next = _pomodoroSession.Advance();
            if (next != null)
            {
                _currentTimer = next;
                UpdateIndicators(next);
                TimerText = FormatTimerText(next.Duration);
                CountdownRequested?.Invoke(this, next);
                if (_activeTask != null)
                {
                    await BroadcastActiveStateAsync(_activeTask, next, "pomodoro-advance", isAutoShuffle: false);
                }
            }
            else
            {
                await CompletePomodoroAsync();
            }

            return;
        }

        await _notifications.ShowToastAsync("Time's up", "Shuffling a new task...", _settings);
        await BroadcastNotificationAsync("Time's up", "Shuffling a new task...", _activeTask?.Id, isReminder: false, delay: TimeSpan.Zero);
        await ShuffleAfterTimeoutAsync();
    }

    private async Task CompletePomodoroAsync()
    {
        int cycles = _pomodoroSession?.CycleCount ?? _settings?.PomodoroCycles ?? 0;
        TimerText = "--:--";
        ShowPomodoroCompletion(cycles);
        _pomodoroSession = null;
        _currentTimer = null;
        CountdownCleared?.Invoke(this, EventArgs.Empty);
        await BroadcastClearedStateAsync("pomodoro-complete");
    }

    public Task ApplyAutoShuffleAsync(TaskItem task, AppSettings settings)
    {
        _settings = settings;
        BindTask(task);

        var effectiveSettings = TaskTimerSettings.Resolve(task, settings);
        var (mode, reminderMinutes, focusMinutes, breakMinutes, pomodoroCycles) = effectiveSettings;

        if (mode == TimerMode.Pomodoro)
        {
            _pomodoroSession = PomodoroSession.Create(focusMinutes, breakMinutes, pomodoroCycles);
            var request = _pomodoroSession.CurrentRequest();
            _currentTimer = request;
            UpdateIndicators(request);
            TimerText = FormatTimerText(request.Duration);
            CountdownRequested?.Invoke(this, request);
        }
        else
        {
            _pomodoroSession = null;
            int minutes = Math.Max(1, reminderMinutes);
            var request = TimerRequest.LongIntervalFromMinutes(minutes);
            _currentTimer = request;
            UpdateIndicators(request);
            TimerText = FormatTimerText(request.Duration);
            CountdownRequested?.Invoke(this, request);
        }

        return Task.CompletedTask;
    }

    internal static string FormatTimerText(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return "00:00";
        }

        return remaining.ToString(@"mm\:ss");
    }

    public void ClearActiveTask()
    {
        ShowDefaultState();
    }

    private void BindTask(TaskItem task)
    {
        _activeTask = task;
        Title = string.IsNullOrWhiteSpace(task.Title) ? "Untitled task" : task.Title;
        Description = string.IsNullOrWhiteSpace(task.Description)
            ? "No description provided."
            : task.Description;
        Schedule = BuildScheduleText(task);
        HasTask = true;
    }

    private void ShowDefaultState()
    {
        ShowMessage(DefaultTitle, DefaultDescription);
    }

    private void ShowMessage(string title, string description)
    {
        ResetTimerState();
        _activeTask = null;
        Title = title;
        Description = description;
        Schedule = DefaultSchedule;
        TimerText = "--:--";
        HasTask = false;
        CountdownCleared?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateIndicators(TimerRequest? request)
    {
        if (request?.Mode == TimerMode.Pomodoro && request.Phase.HasValue)
        {
            IsPomodoroVisible = true;
            PhaseBadge = request.Phase.Value == PomodoroPhase.Focus ? "Focus" : "Break";
            int cycleIndex = Math.Max(1, request.CycleIndex);
            int cycleCount = Math.Max(cycleIndex, request.CycleCount);
            CycleStatus = $"Cycle {cycleIndex} of {cycleCount}";
        }
        else
        {
            IsPomodoroVisible = false;
            PhaseBadge = string.Empty;
            CycleStatus = string.Empty;
        }
    }

    private void ShowPomodoroCompletion(int cycles)
    {
        IsPomodoroVisible = true;
        PhaseBadge = "Complete";
        CycleStatus = cycles > 0
            ? $"{cycles} cycle(s) finished"
            : "Session complete";
    }

    private void ResetTimerState()
    {
        _currentTimer = null;
        _pomodoroSession = null;
        IsPomodoroVisible = false;
        PhaseBadge = string.Empty;
        CycleStatus = string.Empty;
    }

    private async Task SchedulePomodoroNotificationsAsync(TaskItem task, PomodoroSession session, AppSettings settings)
    {
        string displayTitle = string.IsNullOrWhiteSpace(task.Title) ? "Untitled task" : task.Title;
        var schedulingTasks = new List<Task>();
        TimeSpan offset = TimeSpan.Zero;
        bool hasBreak = session.BreakMinutes > 0;
        bool summaryScheduled = false;

        for (int cycle = 1; cycle <= session.CycleCount; cycle++)
        {
            (offset, bool focusSummaryScheduled) = await ScheduleFocusCompletionAsync(
                task,
                session,
                settings,
                displayTitle,
                cycle,
                offset,
                schedulingTasks);
            summaryScheduled |= focusSummaryScheduled;

            if (hasBreak)
            {
                (offset, bool breakSummaryScheduled) = await ScheduleBreakPhaseAsync(
                    task,
                    session,
                    settings,
                    displayTitle,
                    cycle,
                    offset,
                    schedulingTasks);
                summaryScheduled |= breakSummaryScheduled;
            }
        }

        if (!summaryScheduled)
        {
            await ScheduleSummaryAsync(task, session, settings, displayTitle, offset, schedulingTasks);
        }

        if (schedulingTasks.Count > 0)
        {
            await Task.WhenAll(schedulingTasks);
        }
    }

    private async Task<(TimeSpan Offset, bool SummaryScheduled)> ScheduleFocusCompletionAsync(
        TaskItem task,
        PomodoroSession session,
        AppSettings settings,
        string displayTitle,
        int cycle,
        TimeSpan offset,
        List<Task> schedulingTasks)
    {
        offset += TimeSpan.FromMinutes(session.FocusMinutes);
        bool hasBreak = session.BreakMinutes > 0;
        bool isLastCycle = cycle == session.CycleCount;
        bool summaryScheduled = false;

        string focusTitle = $"{displayTitle}: Focus complete";
        string focusMessage = hasBreak
            ? "Take a short break."
            : isLastCycle
                ? "Pomodoro cycles finished!"
                : "Start the next cycle.";

        EnqueuePhaseNotification(settings, focusTitle, focusMessage, offset, schedulingTasks);
        await BroadcastNotificationAsync(focusTitle, focusMessage, task.Id, isReminder: false, delay: offset);

        if (!hasBreak && isLastCycle)
        {
            await ScheduleSummaryAsync(task, session, settings, displayTitle, offset, schedulingTasks);
            summaryScheduled = true;
        }

        return (offset, summaryScheduled);
    }

    private async Task<(TimeSpan Offset, bool SummaryScheduled)> ScheduleBreakPhaseAsync(
        TaskItem task,
        PomodoroSession session,
        AppSettings settings,
        string displayTitle,
        int cycle,
        TimeSpan offset,
        List<Task> schedulingTasks)
    {
        offset += TimeSpan.FromMinutes(session.BreakMinutes);
        bool isLastCycle = cycle == session.CycleCount;
        bool summaryScheduled = false;

        if (isLastCycle)
        {
            await ScheduleSummaryAsync(task, session, settings, displayTitle, offset, schedulingTasks);
            summaryScheduled = true;
        }
        else
        {
            string breakTitle = $"{displayTitle}: Break complete";
            const string breakMessage = "Focus time again.";
            EnqueuePhaseNotification(settings, breakTitle, breakMessage, offset, schedulingTasks);
            await BroadcastNotificationAsync(breakTitle, breakMessage, task.Id, isReminder: false, delay: offset);
        }

        return (offset, summaryScheduled);
    }

    private async Task ScheduleSummaryAsync(
        TaskItem task,
        PomodoroSession session,
        AppSettings settings,
        string displayTitle,
        TimeSpan offset,
        List<Task> schedulingTasks)
    {
        string summaryTitle = $"{displayTitle}: Pomodoro complete";
        string summaryMessage = $"Finished {session.CycleCount} cycle(s).";
        EnqueuePhaseNotification(settings, summaryTitle, summaryMessage, offset, schedulingTasks);
        await BroadcastNotificationAsync(summaryTitle, summaryMessage, task.Id, isReminder: false, delay: offset);
    }

    private void EnqueuePhaseNotification(
        AppSettings settings,
        string title,
        string message,
        TimeSpan offset,
        List<Task> schedulingTasks)
    {
        if (settings.EnableNotifications)
        {
            schedulingTasks.Add(_notifications.NotifyPhaseAsync(title, message, offset, settings));
        }
    }

    private async Task BroadcastActiveStateAsync(TaskItem task, TimerRequest request, string trigger, bool isAutoShuffle)
    {
        if (!CanBroadcast)
        {
            return;
        }

        int seconds = Math.Max(1, (int)Math.Ceiling(request.Duration.TotalSeconds));
        var now = _clock.GetUtcNow();
        var expiresAt = now.AddSeconds(seconds);

        int? phase = null;
        int? cycleIndex = null;
        int? cycleCount = null;
        int? focusMinutes = null;
        int? breakMinutes = null;

        if (request.Mode == TimerMode.Pomodoro && request.Phase.HasValue)
        {
            phase = request.Phase.Value == PomodoroPhase.Break ? 1 : 0;
            cycleIndex = Math.Max(1, request.CycleIndex);
            cycleCount = Math.Max(cycleIndex.Value, request.CycleCount);
            focusMinutes = Math.Max(1, request.FocusMinutes);
            breakMinutes = Math.Max(1, request.BreakMinutes);
        }

        var context = new ShuffleStateChanged.ShuffleDeviceContext(
            _sync!.DeviceId,
            task.Id,
            isAutoShuffle,
            trigger,
            now.UtcDateTime);
        var timer = new ShuffleStateChanged.ShuffleTimerSnapshot(
            seconds,
            expiresAt.UtcDateTime,
            (int)request.Mode,
            phase,
            cycleIndex,
            cycleCount,
            focusMinutes,
            breakMinutes);
        var state = new ShuffleStateChanged(context, timer);

        try
        {
            await _sync.PublishAsync(state).ConfigureAwait(false);
        }
        catch
        {
            // best-effort; ignore failures so local UX stays responsive
        }
    }

    private async Task BroadcastClearedStateAsync(string trigger)
    {
        ClearLocalPersistedState();

        if (!CanBroadcast)
        {
            return;
        }

        var now = _clock.GetUtcNow();
        var context = new ShuffleStateChanged.ShuffleDeviceContext(
            _sync!.DeviceId,
            null,
            isAutoShuffle: false,
            trigger,
            now.UtcDateTime);
        var timer = new ShuffleStateChanged.ShuffleTimerSnapshot(
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
        var state = new ShuffleStateChanged(context, timer);

        try
        {
            await _sync.PublishAsync(state).ConfigureAwait(false);
        }
        catch
        {
            // ignore sync failures
        }
    }

    private async Task BroadcastNotificationAsync(string title, string message, string? taskId, bool isReminder, TimeSpan? delay = null)
    {
        if (!CanBroadcast)
        {
            return;
        }

        var identity = new NotificationBroadcasted.NotificationIdentity(
            Guid.NewGuid().ToString("N"),
            _sync!.DeviceId);
        var content = new NotificationBroadcasted.NotificationContent(title, message);
        var schedule = new NotificationBroadcasted.NotificationSchedule(
            taskId,
            _clock.GetUtcNow().UtcDateTime,
            delay);

        var evt = new NotificationBroadcasted(identity, content, schedule, isReminder);

        try
        {
            await _sync.PublishAsync(evt).ConfigureAwait(false);
        }
        catch
        {
            // ignore network propagation failures
        }
    }

    private static void ClearLocalPersistedState()
    {
        PersistedTimerState.Clear();
        Preferences.Default.Remove(PreferenceKeys.TimerMode);
        Preferences.Default.Remove(PreferenceKeys.PomodoroPhase);
        Preferences.Default.Remove(PreferenceKeys.PomodoroCycle);
        Preferences.Default.Remove(PreferenceKeys.PomodoroTotal);
        Preferences.Default.Remove(PreferenceKeys.PomodoroFocus);
        Preferences.Default.Remove(PreferenceKeys.PomodoroBreak);
    }

    private sealed class PomodoroSession
    {
        public PomodoroSession(int focusMinutes, int breakMinutes, int cycles)
        {
            FocusMinutes = Math.Max(1, focusMinutes);
            BreakMinutes = Math.Max(1, breakMinutes);
            CycleCount = Math.Max(1, cycles);
            CurrentCycle = 1;
            Phase = PomodoroPhase.Focus;
        }

        private PomodoroSession(int focusMinutes, int breakMinutes, int cycles, int currentCycle, PomodoroPhase phase)
        {
            FocusMinutes = Math.Max(1, focusMinutes);
            BreakMinutes = Math.Max(1, breakMinutes);
            CycleCount = Math.Max(1, cycles);
            CurrentCycle = Math.Clamp(currentCycle, 1, CycleCount);
            Phase = phase;

            if (Phase == PomodoroPhase.Break && BreakMinutes <= 0)
            {
                Phase = PomodoroPhase.Focus;
            }
        }

        public int FocusMinutes { get; }

        public int BreakMinutes { get; }

        public int CycleCount { get; }

        public int CurrentCycle { get; private set; }

        public PomodoroPhase Phase { get; private set; }

        public static PomodoroSession Create(AppSettings settings)
            => new PomodoroSession(settings.FocusMinutes, settings.BreakMinutes, settings.PomodoroCycles);

        public static PomodoroSession Create(int focusMinutes, int breakMinutes, int cycles)
            => new PomodoroSession(focusMinutes, breakMinutes, cycles);

        public static PomodoroSession FromState(TimerRequest state)
            => new PomodoroSession(state.FocusMinutes, state.BreakMinutes, state.CycleCount, state.CycleIndex, state.Phase ?? PomodoroPhase.Focus);

        public TimerRequest CurrentRequest()
            => TimerRequest.Pomodoro(CurrentDuration, Phase, CurrentCycle, CycleCount, FocusMinutes, BreakMinutes);

        public TimerRequest? Advance()
        {
            if (Phase == PomodoroPhase.Focus && BreakMinutes > 0)
            {
                Phase = PomodoroPhase.Break;
                return CurrentRequest();
            }

            if (Phase == PomodoroPhase.Focus && BreakMinutes <= 0)
            {
                if (CurrentCycle >= CycleCount)
                {
                    return null;
                }

                CurrentCycle++;
                Phase = PomodoroPhase.Focus;
                return CurrentRequest();
            }

            if (CurrentCycle >= CycleCount)
            {
                return null;
            }

            CurrentCycle++;
            Phase = PomodoroPhase.Focus;
            return CurrentRequest();
        }

        private TimeSpan CurrentDuration => Phase == PomodoroPhase.Focus
            ? TimeSpan.FromMinutes(FocusMinutes)
            : TimeSpan.FromMinutes(BreakMinutes);
    }

    private TaskItem? PickNextCandidate(IList<TaskItem> tasks, AppSettings settings, DateTimeOffset now, string? previousId)
    {
        List<TaskItem> candidatePool = ManualShuffleService.CreateCandidatePool(tasks, settings);
        var chosenClone = _scheduler.PickNextTask(candidatePool, settings, now);
        if (chosenClone == null)
        {
            return null;
        }

        var chosen = FindOriginal(tasks, chosenClone.Id);
        if (chosen == null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(previousId) || !string.Equals(chosen.Id, previousId, StringComparison.Ordinal))
        {
            return chosen;
        }

        var alternatives = tasks
            .Where(t => !string.Equals(t.Id, previousId, StringComparison.Ordinal))
            .ToList();

        if (alternatives.Count == 0)
        {
            return chosen;
        }

        List<TaskItem> alternativePool = ManualShuffleService.CreateCandidatePool(alternatives, settings);
        var alternativeClone = _scheduler.PickNextTask(alternativePool, settings, now);
        if (alternativeClone == null)
        {
            return chosen;
        }

        var alternative = FindOriginal(alternatives, alternativeClone.Id);
        return alternative ?? chosen;
    }

    private static TaskItem? FindOriginal(IEnumerable<TaskItem> tasks, string id)
    {
        return tasks.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));
    }

    private static void EmitTimerResetTelemetry(string reason, TaskItem? task)
    {
        if (task == null)
        {
            Debug.WriteLine($"[ShuffleTask] Timer reset ({reason})");
            return;
        }

        Debug.WriteLine($"[ShuffleTask] Timer reset ({reason}) for task {task.Id} -> status={task.Status}, nextEligible={task.NextEligibleAt:O}");
    }

    private static string BuildScheduleText(TaskItem task)
    {
        string deadline = task.Deadline.HasValue
            ? $"Deadline {task.Deadline:MMM d, yyyy HH:mm}"
            : "No deadline";

        string repeat = task.Repeat switch
        {
            RepeatType.None => "One-off task",
            RepeatType.Daily => "Repeats daily",
            RepeatType.Weekly => $"Weekly on {FormatWeekdays(task.Weekdays)}",
            RepeatType.Interval => $"Every {Math.Max(1, task.IntervalDays)} day(s)",
            _ => "Schedule unknown"
        };

        string allowed = task.AllowedPeriod switch
        {
            AllowedPeriod.Any => "Any time",
            AllowedPeriod.Work => "Work hours",
            AllowedPeriod.OffWork => "Off hours",
            AllowedPeriod.Custom => FormatCustomWindow(task),
            _ => "Any time"
        };

        return $"{deadline} • {repeat} • {allowed}";
    }

    private static string FormatWeekdays(Weekdays weekdays)
    {
        if (weekdays == Weekdays.None)
        {
            return "no specific days";
        }

        var names = new List<string>();

        void Add(Weekdays day, string name)
        {
            if (weekdays.HasFlag(day))
            {
                names.Add(name);
            }
        }

        Add(Weekdays.Mon, "Mon");
        Add(Weekdays.Tue, "Tue");
        Add(Weekdays.Wed, "Wed");
        Add(Weekdays.Thu, "Thu");
        Add(Weekdays.Fri, "Fri");
        Add(Weekdays.Sat, "Sat");
        Add(Weekdays.Sun, "Sun");

        return string.Join(", ", names);
    }

    private static string FormatCustomWindow(TaskItem task)
    {
        if (task.CustomStartTime.HasValue && task.CustomEndTime.HasValue)
        {
            return $"Custom hours ({task.CustomStartTime:hh\\:mm}–{task.CustomEndTime:hh\\:mm})";
        }

        return "Custom hours";
    }
}
