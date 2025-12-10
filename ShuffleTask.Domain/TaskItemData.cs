namespace ShuffleTask.Domain.Entities;

public abstract class TaskItemData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public string? DeviceId { get; set; }

    public string? UserId { get; set; }

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

    public bool AutoShuffleAllowed { get; set; } = true;

    public TimeSpan? CustomStartTime { get; set; }

    public TimeSpan? CustomEndTime { get; set; }

    public bool Paused { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public TaskLifecycleStatus Status { get; set; } = TaskLifecycleStatus.Active;

    public DateTime? SnoozedUntil { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? NextEligibleAt { get; set; }

    // Per-task timer override settings (nullable means use global defaults)
    public int? CustomTimerMode { get; set; }

    public int? CustomReminderMinutes { get; set; }

    public int? CustomFocusMinutes { get; set; }

    public int? CustomBreakMinutes { get; set; }

    public int? CustomPomodoroCycles { get; set; }

    public CutInLineMode CutInLineMode { get; set; }

    /// <summary>
    /// Monotonic event version used for idempotency when applying remote updates.
    /// </summary>
    public int EventVersion { get; set; }

    public void CopyFrom(TaskItemData source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Id = source.Id;
        DeviceId = source.DeviceId;
        UserId = source.UserId;
        Title = source.Title;
        Description = source.Description;
        Importance = source.Importance;
        SizePoints = source.SizePoints;
        Deadline = source.Deadline;
        Repeat = source.Repeat;
        Weekdays = source.Weekdays;
        IntervalDays = source.IntervalDays;
        LastDoneAt = source.LastDoneAt;
        AllowedPeriod = source.AllowedPeriod;
        AutoShuffleAllowed = source.AutoShuffleAllowed;
        CustomStartTime = source.CustomStartTime;
        CustomEndTime = source.CustomEndTime;
        Paused = source.Paused;
        CreatedAt = source.CreatedAt;
        Status = source.Status;
        SnoozedUntil = source.SnoozedUntil;
        CompletedAt = source.CompletedAt;
        NextEligibleAt = source.NextEligibleAt;
        CustomTimerMode = source.CustomTimerMode;
        CustomReminderMinutes = source.CustomReminderMinutes;
        CustomFocusMinutes = source.CustomFocusMinutes;
        CustomBreakMinutes = source.CustomBreakMinutes;
        CustomPomodoroCycles = source.CustomPomodoroCycles;
        CutInLineMode = source.CutInLineMode;

        UpdatedAt = source.UpdatedAt;
        EventVersion = source.EventVersion;
    }
}
