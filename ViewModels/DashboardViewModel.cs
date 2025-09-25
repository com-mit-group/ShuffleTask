using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShuffleTask.Models;
using ShuffleTask.Services;

namespace ShuffleTask.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IStorageService _storage;
    private readonly ISchedulerService _scheduler;
    private readonly INotificationService _notifications;
    private readonly ShuffleCoordinatorService _coordinator;

    private TaskItem? _activeTask;
    private AppSettings? _settings;
    private PomodoroSession? _pomodoroSession;
    private TimerRequest? _currentTimer;

    private const string DefaultTitle = "Shuffle a task";
    private const string DefaultDescription = "Tap Shuffle to pick what comes next.";
    private const string DefaultSchedule = "No schedule yet.";

    public DashboardViewModel(IStorageService storage, ISchedulerService scheduler, INotificationService notifications, ShuffleCoordinatorService coordinator)
    {
        _storage = storage;
        _scheduler = scheduler;
        _notifications = notifications;
        _coordinator = coordinator;

        Title = DefaultTitle;
        Description = DefaultDescription;
        Schedule = DefaultSchedule;
        TimerText = "--:--";
        CycleStatus = string.Empty;
        PhaseBadge = string.Empty;
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
        int BreakMinutes);

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

    [ObservableProperty]
    private string cycleStatus = string.Empty;

    [ObservableProperty]
    private string phaseBadge = string.Empty;

    [ObservableProperty]
    private bool isPomodoroVisible;

    public string? ActiveTaskId => _activeTask?.Id;

    public async Task InitializeAsync()
    {
        await _storage.InitializeAsync();
        if (_settings == null)
        {
            _settings = await _storage.GetSettingsAsync();
        }
        await _notifications.InitializeAsync();
        _coordinator.RegisterDashboard(this);
    }

    private async Task EnsureSettingsAsync()
    {
        if (_settings == null)
        {
            await InitializeAsync();
        }
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

            if (!settings.Active)
            {
                ShowMessage("Scheduling paused", "Enable the scheduler from Settings to shuffle tasks.");
                return;
            }

            var tasks = await _storage.GetTasksAsync();
            var now = DateTime.Now;
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

            BindTask(next);

            if (settings.TimerMode == TimerMode.Pomodoro)
            {
                _pomodoroSession = PomodoroSession.Create(settings);
                var request = _pomodoroSession.CurrentRequest();
                _currentTimer = request;
                UpdateIndicators(request);
                TimerText = FormatTimerText(request.Duration);
                CountdownRequested?.Invoke(this, request);

                if (settings.EnableNotifications)
                {
                    await _notifications.NotifyTaskAsync(next, _pomodoroSession.FocusMinutes, settings);
                    SchedulePomodoroNotifications(next, _pomodoroSession, settings);
                }
            }
            else
            {
                _pomodoroSession = null;
                int minutes = Math.Max(1, settings.ReminderMinutes);
                var request = new TimerRequest(TimeSpan.FromMinutes(minutes), TimerMode.LongInterval, null, 0, 0, 0, 0);
                _currentTimer = request;
                UpdateIndicators(request);
                TimerText = FormatTimerText(request.Duration);
                CountdownRequested?.Invoke(this, request);

                if (settings.EnableNotifications)
                {
                    await _notifications.NotifyTaskAsync(next, minutes, settings);
                }
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
        int cycles = _pomodoroSession?.CycleCount ?? _settings?.PomodoroCycles ?? 0;
        TimerText = "--:--";
        ShowPomodoroCompletion(cycles);
        _pomodoroSession = null;
        _currentTimer = null;
        CountdownCleared?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task ApplyAutoShuffleAsync(TaskItem task, AppSettings settings)
    {
        _settings = settings;
        BindTask(task);

        int minutes = Math.Max(1, settings.ReminderMinutes);
        var duration = TimeSpan.FromMinutes(minutes);
        TimerText = FormatTimerText(duration);
        CountdownRequested?.Invoke(this, duration);

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

    private void SchedulePomodoroNotifications(TaskItem task, PomodoroSession session, AppSettings settings)
    {
        int focusMinutes = session.FocusMinutes;
        int breakMinutes = session.BreakMinutes;
        int cycles = session.CycleCount;

        var focusDuration = TimeSpan.FromMinutes(focusMinutes);
        var breakDuration = TimeSpan.FromMinutes(breakMinutes);
        TimeSpan offset = TimeSpan.Zero;

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
            _ = _notifications.NotifyPhaseAsync(focusTitle, focusMessage, offset, settings);

            if (breakMinutes > 0)
            {
                offset += breakDuration;
                if (cycle == cycles)
                {
                    string summaryTitle = $"{task.Title}: Pomodoro complete";
                    string summaryMessage = $"Finished {cycles} cycle(s).";
                    _ = _notifications.NotifyPhaseAsync(summaryTitle, summaryMessage, offset, settings);
                }
                else
                {
                    string breakTitle = $"{task.Title}: Break complete";
                    const string breakMessage = "Focus time again.";
                    _ = _notifications.NotifyPhaseAsync(breakTitle, breakMessage, offset, settings);
                }
            }
            else if (cycle == cycles)
            {
                string summaryTitle = $"{task.Title}: Pomodoro complete";
                string summaryMessage = $"Finished {cycles} cycle(s).";
                _ = _notifications.NotifyPhaseAsync(summaryTitle, summaryMessage, offset, settings);
            }
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

        public static PomodoroSession FromState(TimerRequest state)
            => new PomodoroSession(state.FocusMinutes, state.BreakMinutes, state.CycleCount, state.CycleIndex, state.Phase ?? PomodoroPhase.Focus);

        public TimerRequest CurrentRequest()
            => new TimerRequest(CurrentDuration, TimerMode.Pomodoro, Phase, CurrentCycle, CycleCount, FocusMinutes, BreakMinutes);

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

    private TaskItem? PickNextCandidate(IList<TaskItem> tasks, AppSettings settings, DateTime now, string? previousId)
    {
        var chosen = _scheduler.PickNextTask(tasks, settings, now);
        if (chosen == null || string.IsNullOrEmpty(previousId) || !string.Equals(chosen.Id, previousId, StringComparison.Ordinal))
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

        var alternative = _scheduler.PickNextTask(alternatives, settings, now);
        return alternative ?? chosen;
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
            AllowedPeriod.Off => "Off days",
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
}
