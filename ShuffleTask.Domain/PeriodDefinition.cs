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
    public const string WeekdaysId = "weekdays";
    public const string WeekendsId = "weekends";
    public const string MorningsId = "mornings";
    public const string EveningsId = "evenings";
    public const string LunchBreakId = "lunch-break";

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

    public static readonly PeriodDefinition Weekdays = new()
    {
        Id = WeekdaysId,
        Name = "Weekdays",
        Weekdays = Weekdays.Mon | Weekdays.Tue | Weekdays.Wed | Weekdays.Thu | Weekdays.Fri,
        IsAllDay = true,
        Mode = PeriodDefinitionMode.None
    };

    public static readonly PeriodDefinition Weekends = new()
    {
        Id = WeekendsId,
        Name = "Weekends",
        Weekdays = Weekdays.Sun | Weekdays.Sat,
        IsAllDay = true,
        Mode = PeriodDefinitionMode.None
    };

    public static readonly PeriodDefinition Mornings = new()
    {
        Id = MorningsId,
        Name = "Mornings",
        Weekdays = AllWeekdays,
        StartTime = new TimeSpan(7, 0, 0),
        EndTime = new TimeSpan(10, 0, 0),
        IsAllDay = false,
        Mode = PeriodDefinitionMode.None
    };

    public static readonly PeriodDefinition Evenings = new()
    {
        Id = EveningsId,
        Name = "Evenings",
        Weekdays = AllWeekdays,
        StartTime = new TimeSpan(18, 0, 0),
        EndTime = new TimeSpan(21, 0, 0),
        IsAllDay = false,
        Mode = PeriodDefinitionMode.None
    };

    public static readonly PeriodDefinition LunchBreak = new()
    {
        Id = LunchBreakId,
        Name = "Lunch break",
        Weekdays = AllWeekdays,
        StartTime = new TimeSpan(12, 0, 0),
        EndTime = new TimeSpan(13, 0, 0),
        IsAllDay = false,
        Mode = PeriodDefinitionMode.None
    };

    private static readonly IReadOnlyDictionary<string, PeriodDefinition> BuiltIns =
        new Dictionary<string, PeriodDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [AnyId] = Any,
            [WorkId] = Work,
            [OffWorkId] = OffWork,
            [WeekdaysId] = Weekdays,
            [WeekendsId] = Weekends,
            [MorningsId] = Mornings,
            [EveningsId] = Evenings,
            [LunchBreakId] = LunchBreak
        };

    public static IReadOnlyList<PeriodDefinition> CreatePresetDefinitions()
    {
        return new List<PeriodDefinition>
        {
            CreatePreset(Weekdays),
            CreatePreset(Weekends),
            CreatePreset(Mornings),
            CreatePreset(Evenings),
            CreatePreset(LunchBreak)
        };
    }

    public static bool TryGet(string? id, out PeriodDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(id) && BuiltIns.TryGetValue(id, out definition!))
        {
            return true;
        }

        definition = Any;
        return false;
    }

    private static PeriodDefinition CreatePreset(PeriodDefinition template)
    {
        return new PeriodDefinition
        {
            Id = template.Id,
            Name = template.Name,
            Weekdays = template.Weekdays,
            StartTime = template.StartTime,
            EndTime = template.EndTime,
            IsAllDay = template.IsAllDay,
            Mode = template.Mode
        };
    }
}
