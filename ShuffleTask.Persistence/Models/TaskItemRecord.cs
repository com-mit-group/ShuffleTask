using SQLite;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Persistence.Models;

[Table("TaskItem")]
internal sealed class TaskItemRecord
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    [Indexed]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int Importance { get; set; }

    public double SizePoints { get; set; } = 3.0;

    public DateTime? Deadline { get; set; }

    public RepeatType Repeat { get; set; }

    public Weekdays Weekdays { get; set; }

    public int IntervalDays { get; set; }

    public DateTime? LastDoneAt { get; set; }

    public AllowedPeriod AllowedPeriod { get; set; }

    public bool Paused { get; set; }

    public DateTime CreatedAt { get; set; }

    public TaskLifecycleStatus Status { get; set; } = TaskLifecycleStatus.Active;

    public DateTime? SnoozedUntil { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? NextEligibleAt { get; set; }

    public static TaskItemRecord FromDomain(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return new TaskItemRecord
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

    public TaskItem ToDomain()
    {
        return new TaskItem
        {
            Id = Id,
            Title = Title,
            Description = Description,
            Importance = Importance,
            SizePoints = SizePoints,
            Deadline = Deadline,
            Repeat = Repeat,
            Weekdays = Weekdays,
            IntervalDays = IntervalDays,
            LastDoneAt = LastDoneAt,
            AllowedPeriod = AllowedPeriod,
            Paused = Paused,
            CreatedAt = CreatedAt,
            Status = Status,
            SnoozedUntil = SnoozedUntil,
            CompletedAt = CompletedAt,
            NextEligibleAt = NextEligibleAt
        };
    }
}
