namespace ShuffleTask.Persistence.Models;

internal sealed class PeriodValidationRow
{
    public long RowId { get; set; }
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public int? Weekdays { get; set; }
    public int? IsAllDay { get; set; }
    public int? Mode { get; set; }
}
