using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShuffleTask.Models;
using ShuffleTask.Services;

namespace ShuffleTask.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly StorageService _storage;
    private readonly SchedulerService _scheduler;
    private readonly NotificationService _notifications;

    private TaskItem? _activeTask;
    private AppSettings? _settings;

    private const string DefaultTitle = "Shuffle a task";
    private const string DefaultDescription = "Tap Shuffle to pick what comes next.";
    private const string DefaultSchedule = "No schedule yet.";

    public DashboardViewModel(StorageService storage, SchedulerService scheduler, NotificationService notifications)
    {
        _storage = storage;
        _scheduler = scheduler;
        _notifications = notifications;

        Title = DefaultTitle;
        Description = DefaultDescription;
        Schedule = DefaultSchedule;
        TimerText = "--:--";
    }

    public event EventHandler<TimeSpan>? CountdownRequested;
    public event EventHandler? CountdownCleared;

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

    public string? ActiveTaskId => _activeTask?.Id;

    public async Task InitializeAsync()
    {
        await _storage.InitializeAsync();
        if (_settings == null)
        {
            _settings = await _storage.GetSettingsAsync();
        }
        await _notifications.InitializeAsync();
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

            int minutes = Math.Max(1, settings.ReminderMinutes);
            var duration = TimeSpan.FromMinutes(minutes);
            TimerText = FormatTimerText(duration);
            CountdownRequested?.Invoke(this, duration);

            if (settings.EnableNotifications)
            {
                await _notifications.NotifyTaskAsync(next, minutes, settings);
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

        await _storage.MarkTaskDoneAsync(_activeTask.Id);
        ShowMessage("Task complete", "Shuffle another task when you're ready.");
    }

    [RelayCommand]
    private Task SnoozeAsync()
    {
        ShowMessage("Task snoozed", "Shuffle another task when you're ready.");
        return Task.CompletedTask;
    }

    public async Task<bool> RestoreTaskAsync(string? taskId, TimeSpan? remaining)
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
        if (remaining.HasValue)
        {
            TimerText = FormatTimerText(remaining.Value);
        }

        return true;
    }

    public async Task NotifyTimeUpAsync()
    {
        await EnsureSettingsAsync();
        if (_settings != null)
        {
            await _notifications.ShowToastAsync("Time's up", "Shuffling a new task...", _settings);
        }
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
        _activeTask = null;
        Title = title;
        Description = description;
        Schedule = DefaultSchedule;
        TimerText = "--:--";
        HasTask = false;
        CountdownCleared?.Invoke(this, EventArgs.Empty);
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
