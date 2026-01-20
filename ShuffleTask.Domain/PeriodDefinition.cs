namespace ShuffleTask.Domain.Entities;

[Flags]
public enum PeriodDefinitionMode
{
    None = 0,
    AlignWithWorkHours = 1,
    OffWorkRelativeToWorkHours = 2
}

public class PeriodDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public Weekdays Weekdays { get; set; }

    public TimeSpan? StartTime { get; set; }

    public TimeSpan? EndTime { get; set; }

    public bool IsAllDay { get; set; }

    public PeriodDefinitionMode Mode { get; set; }
}

public static class PeriodDefinitionCatalog
{
    public const string AnyId = "any";
    public const string WorkId = "work";
    public const string OffWorkId = "off-work";

    public static readonly Weekdays AllWeekdays =
        Weekdays.Sun | Weekdays.Mon | Weekdays.Tue | Weekdays.Wed | Weekdays.Thu | Weekdays.Fri | Weekdays.Sat;

    public static readonly PeriodDefinition Any = new()
    {
        Id = AnyId,
        Name = "Any time",
        Weekdays = AllWeekdays,
        IsAllDay = true,
        Mode = PeriodDefinitionMode.None
    };

    public static readonly PeriodDefinition Work = new()
    {
        Id = WorkId,
        Name = "Work hours",
        Weekdays = Weekdays.Mon | Weekdays.Tue | Weekdays.Wed | Weekdays.Thu | Weekdays.Fri,
        IsAllDay = false,
        Mode = PeriodDefinitionMode.AlignWithWorkHours
    };

    public static readonly PeriodDefinition OffWork = new()
    {
        Id = OffWorkId,
        Name = "Off hours",
        Weekdays = AllWeekdays,
        IsAllDay = false,
        Mode = PeriodDefinitionMode.AlignWithWorkHours | PeriodDefinitionMode.OffWorkRelativeToWorkHours
    };

    private static readonly IReadOnlyDictionary<string, PeriodDefinition> BuiltIns =
        new Dictionary<string, PeriodDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [AnyId] = Any,
            [WorkId] = Work,
            [OffWorkId] = OffWork
        };

    public static bool TryGet(string? id, out PeriodDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(id) && BuiltIns.TryGetValue(id, out definition!))
        {
            return true;
        }

        definition = Any;
        return false;
    }
}
