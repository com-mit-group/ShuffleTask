using System;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Services;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.ViewModels;

public partial class TasksViewModel : ObservableObject
{
    private readonly IStorageService _storage;
    private readonly TimeProvider _clock;

    public TasksViewModel(IStorageService storage, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(storage);
        _storage = storage;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public ObservableCollection<TaskListItem> Tasks { get; } = [];

    [ObservableProperty]
    private bool isBusy;

    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _storage.InitializeAsync();
            List<TaskItem> items = await _storage.GetTasksAsync();
            AppSettings settings = await _storage.GetSettingsAsync();
            DateTimeOffset now = _clock.GetUtcNow();

            Tasks.Clear();
            foreach (TaskListItem? entry in items
                .Select(task => TaskListItem.From(task, settings, now))
                .OrderByDescending(x => x.PriorityScore))
            {
                Tasks.Add(entry);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task TogglePauseAsync(TaskItem task)
    {
        task.Paused = !task.Paused;
        await _storage.UpdateTaskAsync(task);
        await LoadAsync();
    }

    public async Task ResumeAsync(TaskItem task)
    {
        if (task is null)
        {
            return;
        }

        await _storage.ResumeTaskAsync(task.Id);
        await LoadAsync();
    }

    public async Task DeleteAsync(TaskItem task)
    {
        await _storage.DeleteTaskAsync(task.Id);
        await LoadAsync();
    }

}

public class TaskListItem
{
    public TaskItem Task { get; }

    public ImportanceUrgencyScore Score { get; }

    public double PriorityScore => Score.CombinedScore;

    public string ScoreText => $"Score {PriorityScore:0.#}";

    public string Title => string.IsNullOrWhiteSpace(Task.Title) ? "Untitled" : Task.Title;

    public string Description => string.IsNullOrWhiteSpace(Task.Description) ? "No description" : Task.Description;

    public string RepeatText { get; }

    public string ScheduleText { get; }

    public string ImportanceText { get; }

    public string AllowedPeriodText { get; }

    public string StatusText { get; }

    public bool HasStatusBadge { get; }

    public string StatusBackgroundColor { get; }

    public string StatusTextColor { get; }

    public bool CanResume { get; }

    private TaskListItem(
        TaskItem task,
        string repeatText,
        string scheduleText,
        string importanceText,
        string allowedPeriodText,
        TaskStatusPresentation status, ImportanceUrgencyScore score)
    {
        Task = task;
        RepeatText = repeatText;
        ScheduleText = scheduleText;
        ImportanceText = importanceText;
        AllowedPeriodText = allowedPeriodText;
        StatusText = status.Text;
        HasStatusBadge = status.HasBadge;
        StatusBackgroundColor = status.BackgroundColor;
        StatusTextColor = status.TextColor;
        CanResume = status.CanResume;
        Score = score;
    }

    public static TaskListItem From(TaskItem task, AppSettings settings, DateTimeOffset now)
    {
        string repeat = task.Repeat switch
        {
            RepeatType.None => "One-off",
            RepeatType.Daily => "Daily",
            RepeatType.Weekly => $"Weekly ({FormatWeekdays(task.Weekdays)})",
            RepeatType.Interval => $"Every {Math.Max(1, task.IntervalDays)} day(s)",
            _ => task.Repeat.ToString()
        };

        string schedule = task.Deadline.HasValue
            ? $"Due {FormatAbsolute(task.Deadline)}"
            : "No deadline";

        int importance = Math.Clamp(task.Importance, 1, 5);
        string importanceStars = new string('★', importance).PadRight(5, '☆');
        string importanceText = $"Importance: {importanceStars} ({importance}/5)";

        string allowedPeriodText = task.AllowedPeriod switch
        {
            AllowedPeriod.Any => "Auto shuffle: Any time",
            AllowedPeriod.Work => "Auto shuffle: Work hours",
            AllowedPeriod.OffWork => "Auto shuffle: Off hours",
            _ => "Auto shuffle: Any time"
        };

        TaskStatusPresentation status = BuildStatusPresentation(task, now);
        ImportanceUrgencyScore score = ImportanceUrgencyCalculator.Calculate(task, now, settings);

        return new TaskListItem(
            task,
            repeat,
            schedule,
            importanceText,
            allowedPeriodText,
            status, score);
    }

    private static TaskStatusPresentation BuildStatusPresentation(TaskItem task, DateTimeOffset reference)
    {
        if (task.Paused)
        {
            return new TaskStatusPresentation("Paused", true, "#FEE2E2", "#C53030", false);
        }

        return task.Status switch
        {
            TaskLifecycleStatus.Active => TaskStatusPresentation.Active,
            TaskLifecycleStatus.Snoozed => BuildSnoozedPresentation(task, reference),
            TaskLifecycleStatus.Completed => BuildCompletedPresentation(task, reference),
            _ => TaskStatusPresentation.Active
        };
    }

    private static TaskStatusPresentation BuildSnoozedPresentation(TaskItem task, DateTimeOffset reference)
    {
        string until = FormatRelative(task.SnoozedUntil ?? task.NextEligibleAt, reference);
        string text = string.IsNullOrEmpty(until) ? "Snoozed" : $"Snoozed until {until}";
        return new TaskStatusPresentation(text, true, "#FEF3C7", "#975A16", true);
    }

    private static TaskStatusPresentation BuildCompletedPresentation(TaskItem task, DateTimeOffset reference)
    {
        bool oneOff = task.Repeat == RepeatType.None;
        string next = FormatRelative(task.NextEligibleAt, reference);
        string text = "Completed";

        if (!oneOff && !string.IsNullOrEmpty(next))
        {
            text = $"Completed • next at {next}";
        }

        return new TaskStatusPresentation(text, true, "#C6F6D5", "#276749", true);
    }

    private static string FormatRelative(DateTime? value, DateTimeOffset reference)
    {
        if (!value.HasValue)
        {
            return string.Empty;
        }

        DateTimeOffset? target = EnsureUtc(value);
        if (target == null)
        {
            return string.Empty;
        }

        DateTimeOffset referenceLocal = TimeZoneInfo.ConvertTime(reference, TimeZoneInfo.Local);
        DateTimeOffset targetLocal = TimeZoneInfo.ConvertTime(target.Value, TimeZoneInfo.Local);

        DateTime referenceDate = referenceLocal.Date;
        if (targetLocal.Date == referenceDate)
        {
            return targetLocal.ToString("h:mm tt", CultureInfo.CurrentCulture);
        }

        if (targetLocal.Date == referenceDate.AddDays(1))
        {
            return $"tomorrow {targetLocal.ToString("h:mm tt", CultureInfo.CurrentCulture)}";
        }

        return targetLocal.ToString("MMM d h:mm tt", CultureInfo.CurrentCulture);
    }

    private static string FormatAbsolute(DateTime? value)
    {
        if (!value.HasValue)
        {
            return "--";
        }

        DateTimeOffset? target = EnsureUtc(value);
        if (target == null)
        {
            return "--";
        }

        DateTimeOffset local = TimeZoneInfo.ConvertTime(target.Value, TimeZoneInfo.Local);
        return local.ToString("MMM d, yyyy HH:mm", CultureInfo.CurrentCulture);
    }

    private static DateTimeOffset? EnsureUtc(DateTime? value)
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

    private static string FormatWeekdays(Weekdays weekdays)
    {
        if (weekdays == Weekdays.None)
        {
            return "--";
        }

        var names = new List<string>();
        void Add(Weekdays day, string label)
        {
            if (weekdays.HasFlag(day))
            {
                names.Add(label);
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

    private sealed record TaskStatusPresentation(
        string Text,
        bool HasBadge,
        string BackgroundColor,
        string TextColor,
        bool CanResume)
    {
        public static TaskStatusPresentation Active { get; } = new("Active", false, "#E2E8F0", "#2D3748", false);
    }
}
