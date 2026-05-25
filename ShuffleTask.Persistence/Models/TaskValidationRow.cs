namespace ShuffleTask.Persistence.Models;

internal sealed class TaskValidationRow
{
    public long RowId { get; set; }
    public string? Id { get; set; }
    public string? DeviceId { get; set; }
    public string? UserId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Deadline { get; set; }
    public string? LastDoneAt { get; set; }
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
    public string? SnoozedUntil { get; set; }
    public string? CompletedAt { get; set; }
    public string? NextEligibleAt { get; set; }
    public int? Status { get; set; }
    public int? Repeat { get; set; }
    public int? Weekdays { get; set; }
    public int? IntervalDays { get; set; }
    public int? AllowedPeriod { get; set; }
    public int? CutInLineMode { get; set; }
    public int? EventVersion { get; set; }
    public int? CustomTimerMode { get; set; }
    public int? CustomReminderMinutes { get; set; }
    public int? CustomFocusMinutes { get; set; }
    public int? CustomBreakMinutes { get; set; }
    public int? CustomPomodoroCycles { get; set; }
}
