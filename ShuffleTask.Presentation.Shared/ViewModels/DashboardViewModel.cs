using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Services;
using ShuffleTask.Application.Utilities;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Presentation.Models;
using ShuffleTask.Presentation.Services;
using ShuffleTask.Presentation.Utilities;

namespace ShuffleTask.ViewModels;

public sealed class DashboardViewModelDependencies
{
    public required IStorageService Storage { get; init; }
    public required ISchedulerService Scheduler { get; init; }
    public required INotificationService Notifications { get; init; }
    public required ShuffleCoordinatorService Coordinator { get; init; }
    public required TimeProvider Clock { get; init; }
    public required INetworkSyncService NetworkSyncService { get; init; }
    public required AppSettings Settings { get; init; }
    public IShuffleLogger? Logger { get; init; }
}

public partial class DashboardViewModel : ObservableObject
{
    private readonly IStorageService _storage;
    private readonly ISchedulerService _scheduler;
    private readonly INetworkSyncService _networkSyncService;
    private readonly INotificationService _notifications;
    private readonly ShuffleCoordinatorService _coordinator;
    private readonly TimeProvider _clock;
    private readonly AppSettings _settings;
    private readonly IShuffleLogger? _logger;

    private TaskItem? _activeTask;
    private PomodoroSession? _pomodoroSession;
    private TimerRequest? _currentTimer;
    private bool _isInitialized;
    private bool _isInitializing;

    private const string DefaultTitle = "Shuffle a task";
    private const string DefaultDescription = "Tap Shuffle to pick what comes next.";
    private const string DefaultSchedule = "No schedule yet.";

    public DashboardViewModel(DashboardViewModelDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        _storage = dependencies.Storage;
        _scheduler = dependencies.Scheduler;
        _notifications = dependencies.Notifications;
        _coordinator = dependencies.Coordinator;
        _clock = dependencies.Clock;
        _settings = dependencies.Settings;

        Title = DefaultTitle;
        Description = DefaultDescription;
        Schedule = DefaultSchedule;
        TimerText = "--:--";
        CycleStatus = string.Empty;
        PhaseBadge = string.Empty;
        _networkSyncService = dependencies.NetworkSyncService;
        _logger = dependencies.Logger;
    }

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
    private string title;

    [ObservableProperty]
    private string description;

    [ObservableProperty]
    private string schedule;

    [ObservableProperty]
    private string timerText;

    [ObservableProperty]
    private bool hasTask;

    [ObservableProperty]
    private bool isBusy;

    public OperationState OperationState { get; } = new();

    [ObservableProperty]
    private string cycleStatus = string.Empty;

    [ObservableProperty]
    private string phaseBadge = string.Empty;

    [ObservableProperty]
    private bool isPomodoroVisible;

    public string? ActiveTaskId => _activeTask?.Id;
#pragma warning disable S2325 // These bindable properties depend on source-generated instance properties.
    public bool CanActOnTask => HasTask && !IsBusy;
    public bool CanShuffle => !IsBusy;
#pragma warning restore S2325

    partial void OnHasTaskChanged(bool value) => OnPropertyChanged(nameof(CanActOnTask));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanActOnTask));
        OnPropertyChanged(nameof(CanShuffle));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized || _isInitializing)
        {
            return;
        }

        _isInitializing = true;
        OperationState.SetLoading("Preparing the dashboard…");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _storage.InitializeAsync();
            await _notifications.InitializeAsync();
            _coordinator.RegisterDashboard(this);
            _isInitialized = true;
            OperationState.SetSuccess("Dashboard ready.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            OperationState.SetTransientFailure(
                "Dashboard setup was canceled. Try again.",
                null,
                InitializeAsync,
                isBlocking: !HasTask);
        }
        catch (Exception ex)
        {
            _logger?.LogOperation(LogLevel.Error, "InitializeDashboard", "Failed to initialize dashboard services.", ex);
            OperationState.SetTransientFailure(
                "The dashboard could not start. Check storage and notification access, then retry.",
                null,
                InitializeAsync,
                isBlocking: !HasTask);
        }
        finally
        {
            _isInitializing = false;
        }
    }

    [RelayCommand]
    private Task ShuffleAsync(CancellationToken cancellationToken) => ShuffleInternalAsync(allowRepeat: false, cancellationToken);

    public Task ShuffleAfterTimeoutAsync() => ShuffleInternalAsync(allowRepeat: true);

    private async Task ShuffleInternalAsync(bool allowRepeat, CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        OperationState.SetLoading("Finding the next task…");
        var context = new ShuffleOperationContext();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ExecuteShuffleSelectionAsync(allowRepeat, context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            OperationState.SetTransientFailure(
                context.LocalDataSaved == true
                    ? "Shuffle was canceled after local task data was saved. Retry to refresh the selection."
                    : "Shuffle was canceled. No local task data was saved.",
                context.LocalDataSaved,
                token => ShuffleInternalAsync(allowRepeat, token),
                isBlocking: !HasTask);
        }
        catch (Exception ex)
        {
            _logger?.LogOperation(LogLevel.Error, "ShuffleTask", "Failed while selecting or notifying the next task.", ex);
            OperationState.SetTransientFailure(
                context.LocalDataSaved == true
                    ? "The operation failed after local task data was saved. Retry the remaining work."
                    : "A task could not be selected. No local task data was saved. Try again.",
                context.LocalDataSaved,
                token => ShuffleInternalAsync(allowRepeat, token),
                isBlocking: !HasTask);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteShuffleSelectionAsync(
        bool allowRepeat,
        ShuffleOperationContext context,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        if (!_isInitialized)
        {
            return;
        }

        AppSettings settings = _settings;
        if (!settings.Active)
        {
            ShowMessage("Scheduling paused", "Enable the scheduler from Settings to shuffle tasks.");
            OperationState.SetValidation("Scheduling is paused. Enable it in Settings before shuffling.");
            return;
        }

        TaskItem? next = await SelectNextTaskAsync(settings, allowRepeat);
        if (next == null)
        {
            return;
        }

        await ClearOneTimePriorityAsync(next, context);
        BindTask(next);
        await StartTaskTimerAsync(next, settings);
        OperationState.SetSuccess("Next task selected.", context.LocalDataSaved);
    }

    private async Task<TaskItem?> SelectNextTaskAsync(AppSettings settings, bool allowRepeat)
    {
        var network = settings.Network;
        var tasks = await _storage.GetTasksAsync(network?.UserId, network?.DeviceId ?? string.Empty);
        DateTimeOffset now = _clock.GetUtcNow();
        string? previousId = _activeTask?.Id;
        TaskItem? next = PickNextCandidate(tasks, settings, now, previousId);

        if (next == null)
        {
            ShowMessage("No tasks ready", "Add a task or adjust filters to get started.");
            OperationState.SetEmpty("No tasks are ready. Add a task or adjust its schedule.");
            return null;
        }

        bool isSameTask = !string.IsNullOrEmpty(previousId) && next.Id == previousId;
        if (isSameTask && !allowRepeat)
        {
            OperationState.SetSuccess("The current task is still the best available task.");
            return null;
        }

        return next;
    }

    private async Task ClearOneTimePriorityAsync(TaskItem task, ShuffleOperationContext context)
    {
        bool clearsOneTimePriority = task.CutInLineMode == CutInLineMode.Once;
        try
        {
            bool cleared = await CutInLineUtilities.ClearCutInLineOnceAsync(task, _storage);
            context.LocalDataSaved = clearsOneTimePriority ? cleared : null;
        }
        catch
        {
            context.LocalDataSaved = false;
            throw;
        }
    }

    private async Task StartTaskTimerAsync(TaskItem task, AppSettings settings)
    {
        var (mode, reminderMinutes, focusMinutes, breakMinutes, pomodoroCycles) = TaskTimerSettings.Resolve(task, settings);
        if (mode == TimerMode.Pomodoro)
        {
            await StartPomodoroTimerAsync(task, settings, focusMinutes, breakMinutes, pomodoroCycles);
            return;
        }

        _pomodoroSession = null;
        int minutes = Math.Max(1, reminderMinutes);
        var request = TimerRequest.LongIntervalFromMinutes(minutes);
        StartCountdown(request);

        if (settings.EnableNotifications)
        {
            await _notifications.NotifyTaskAsync(task, minutes, settings);
        }
    }

    private async Task StartPomodoroTimerAsync(
        TaskItem task,
        AppSettings settings,
        int focusMinutes,
        int breakMinutes,
        int pomodoroCycles)
    {
        _pomodoroSession = PomodoroSession.Create(focusMinutes, breakMinutes, pomodoroCycles);
        TimerRequest request = _pomodoroSession.CurrentRequest();
        StartCountdown(request);

        if (settings.EnableNotifications)
        {
            await _notifications.NotifyTaskAsync(task, _pomodoroSession.FocusMinutes, settings);
            await SchedulePomodoroNotificationsAsync(task, _pomodoroSession, settings);
        }
    }

    private void StartCountdown(TimerRequest request)
    {
        _currentTimer = request;
        UpdateIndicators(request);
        TimerText = FormatTimerText(request.Duration);
        CountdownRequested?.Invoke(this, request);
    }

    private sealed class ShuffleOperationContext
    {
        public bool? LocalDataSaved { get; set; }
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

        await _notifications.CancelAllAsync();
        _coordinator.SuspendInProcessTimer();

        var snapshot = _activeTask;
        ShowMessage("Task complete", "Shuffle another task when you're ready.");
        EmitTimerResetTelemetry("done", snapshot);
    }

    [RelayCommand]
    private async Task SnoozeAsync()
    {
        if (_activeTask == null)
        {
            return;
        }

        await InitializeAsync();
        var settings = _settings;

        int snoozeMinutes = Math.Max(15, settings.MinGapMinutes);
        var duration = TimeSpan.FromMinutes(snoozeMinutes);

        var updated = await _storage.SnoozeTaskAsync(_activeTask.Id, duration);

        if (updated is not null)
        {
            _activeTask = updated;
            await _networkSyncService.PublishTaskUpsertAsync(_activeTask);
        }

        await _notifications.CancelAllAsync();
        _coordinator.SuspendInProcessTimer();

        var snapshot = _activeTask;
        ShowMessage("Task snoozed", "Shuffle another task when you're ready.");
        EmitTimerResetTelemetry("snooze", snapshot);
    }

    public async Task<bool> RestoreTaskAsync(string? taskId, TimeSpan? remaining, TimerRequest? timerState)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            ShowDefaultState();
            return false;
        }

        await InitializeAsync();
        var task = await _storage.GetTaskAsync(taskId);
        if (task == null || task.Status == TaskLifecycleStatus.Completed || task.Status == TaskLifecycleStatus.Snoozed)
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
        await InitializeAsync();

        if (_currentTimer?.Mode == TimerMode.Pomodoro && _pomodoroSession != null && _activeTask != null)
        {
            var next = _pomodoroSession.Advance();
            if (next != null)
            {
                _currentTimer = next;
                UpdateIndicators(next);
                TimerText = FormatTimerText(next.Duration);
                CountdownRequested?.Invoke(this, next);
            }
            else
            {
                await CompletePomodoroAsync();
            }

            return;
        }

        await _notifications.ShowToastAsync("Time's up", "Shuffling a new task...", _settings);
        await ShuffleAfterTimeoutAsync();
    }

    private Task CompletePomodoroAsync()
    {
        int cycles = _pomodoroSession?.CycleCount ?? _settings.PomodoroCycles;
        TimerText = "--:--";
        ShowPomodoroCompletion(cycles);
        _pomodoroSession = null;
        _currentTimer = null;
        CountdownCleared?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task ApplyAutoOrCrossDeviceShuffleAsync(TaskItem task, AppSettings settings)
    {
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

    public static string FormatTimerText(TimeSpan remaining)
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

#pragma warning disable S2325 // These methods update source-generated instance properties used by the UI.
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
#pragma warning restore S2325

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
        int focusMinutes = session.FocusMinutes;
        int breakMinutes = session.BreakMinutes;
        int cycles = session.CycleCount;

        var focusDuration = TimeSpan.FromMinutes(focusMinutes);
        var breakDuration = TimeSpan.FromMinutes(breakMinutes);
        TimeSpan offset = TimeSpan.Zero;
        var schedulingTasks = new List<Task>();

        for (int cycle = 1; cycle <= cycles; cycle++)
        {
            offset += focusDuration;
            string focusTitle = $"{task.Title}: Focus complete";
            string focusMessage;
            if (breakMinutes > 0)
            {
                focusMessage = "Take a short break.";
            }
            else
            {
                focusMessage = cycle < cycles ? "Start the next cycle." : "Pomodoro cycles finished!";
            }
            schedulingTasks.Add(_notifications.NotifyPhaseAsync(focusTitle, focusMessage, offset, settings));

            if (breakMinutes > 0)
            {
                offset += breakDuration;
                if (cycle == cycles)
                {
                    string summaryTitle = $"{task.Title}: Pomodoro complete";
                    string summaryMessage = $"Finished {cycles} cycle(s).";
                    schedulingTasks.Add(_notifications.NotifyPhaseAsync(summaryTitle, summaryMessage, offset, settings));
                }
                else
                {
                    string breakTitle = $"{task.Title}: Break complete";
                    const string breakMessage = "Focus time again.";
                    schedulingTasks.Add(_notifications.NotifyPhaseAsync(breakTitle, breakMessage, offset, settings));
                }
            }
            else if (cycle == cycles)
            {
                string summaryTitle = $"{task.Title}: Pomodoro complete";
                string summaryMessage = $"Finished {cycles} cycle(s).";
                schedulingTasks.Add(_notifications.NotifyPhaseAsync(summaryTitle, summaryMessage, offset, settings));
            }
        }

        if (schedulingTasks.Count > 0)
        {
            await Task.WhenAll(schedulingTasks);
        }
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

        string allowed = PeriodDefinitionFormatter.FormatAllowedPeriodLabel(task);

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

    
}
