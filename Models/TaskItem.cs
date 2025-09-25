using SQLite;

namespace ShuffleTask.Models;

public class TaskItem
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    [Indexed]
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public int Importance { get; set; } // 1..5

    public double SizePoints { get; set; } = 3.0; // story points style estimate

    public DateTime? Deadline { get; set; }

    public RepeatType Repeat { get; set; }

    public Weekdays Weekdays { get; set; } // for Weekly

    public int IntervalDays { get; set; } // for Interval

    public DateTime? LastDoneAt { get; set; }

    public AllowedPeriod AllowedPeriod { get; set; } // Any/Work/Off/OffWork

    public bool Paused { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public TaskLifecycleStatus Status { get; set; } = TaskLifecycleStatus.Active;

    public DateTime? SnoozedUntil { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? NextEligibleAt { get; set; }

    public static TaskItem Clone(TaskItem task)
    {
        return new TaskItem
        {
            Id = task.Id,
            Title = task.Title,
            Description = task.Description,
            Importance = task.Importance,
            SizePoints = task.SizePoints,
            Deadline = task.Deadline,
            Repeat = task.Repeat,
            Weekdays = task.Weekdays,
            IntervalDays = task.IntervalDays,
            LastDoneAt = task.LastDoneAt,
            AllowedPeriod = task.AllowedPeriod,
            Paused = task.Paused,
            CreatedAt = task.CreatedAt,
            Status = task.Status,
            SnoozedUntil = task.SnoozedUntil,
            CompletedAt = task.CompletedAt,
            NextEligibleAt = task.NextEligibleAt
        };
    }
}
