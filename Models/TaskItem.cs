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

    public DateTime? Deadline { get; set; }

    public RepeatType Repeat { get; set; }

    public Weekdays Weekdays { get; set; } // for Weekly

    public int IntervalDays { get; set; } // for Interval

    public DateTime? LastDoneAt { get; set; }

    public AllowedPeriod AllowedPeriod { get; set; } // Any/Work/Off/OffWork

    public bool Paused { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
