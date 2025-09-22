using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ShuffleTask.Models;
using ShuffleTask.Services;

namespace ShuffleTask.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly StorageService _storage;

    public MainViewModel(StorageService storage)
    {
        _storage = storage;
    }

    public ObservableCollection<TaskListItem> Tasks { get; } = new();

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
            var items = await _storage.GetTasksAsync();

            Tasks.Clear();
            foreach (var task in items)
            {
                Tasks.Add(TaskListItem.From(task));
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

    public async Task DeleteAsync(TaskItem task)
    {
        await _storage.DeleteTaskAsync(task.Id);
        await LoadAsync();
    }

    public static TaskItem Clone(TaskItem task)
    {
        return new TaskItem
        {
            Id = task.Id,
            Title = task.Title,
            Description = task.Description,
            Importance = task.Importance,
            Deadline = task.Deadline,
            Repeat = task.Repeat,
            Weekdays = task.Weekdays,
            IntervalDays = task.IntervalDays,
            LastDoneAt = task.LastDoneAt,
            AllowedPeriod = task.AllowedPeriod,
            Paused = task.Paused,
            CreatedAt = task.CreatedAt
        };
    }
}

public class TaskListItem
{
    public TaskItem Task { get; }

    public string Title => string.IsNullOrWhiteSpace(Task.Title) ? "Untitled" : Task.Title;

    public string Description => string.IsNullOrWhiteSpace(Task.Description) ? "No description" : Task.Description;

    public string RepeatText { get; }

    public string ScheduleText { get; }

    public string ImportanceText { get; }

    public string AllowedPeriodText { get; }

    public string StatusText => Task.Paused ? "Paused" : "Active";

    private TaskListItem(TaskItem task, string repeatText, string scheduleText, string importanceText, string allowedPeriodText)
    {
        Task = task;
        RepeatText = repeatText;
        ScheduleText = scheduleText;
        ImportanceText = importanceText;
        AllowedPeriodText = allowedPeriodText;
    }

    public static TaskListItem From(TaskItem task)
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
            ? $"Due {task.Deadline:MMM d, yyyy HH:mm}"
            : "No deadline";

        int importance = Math.Clamp(task.Importance, 1, 5);
        string importanceStars = new string('★', importance).PadRight(5, '☆');
        string importanceText = $"Importance: {importanceStars} ({importance}/5)";

        string allowedPeriodText = task.AllowedPeriod switch
        {
            AllowedPeriod.Any => "Auto shuffle: Any time",
            AllowedPeriod.Work => "Auto shuffle: Work hours",
            AllowedPeriod.OffWork => "Auto shuffle: Off hours",
            AllowedPeriod.Off => "Auto shuffle: Off days",
            _ => "Auto shuffle: Any time"
        };

        return new TaskListItem(task, repeat, schedule, importanceText, allowedPeriodText);
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
}
