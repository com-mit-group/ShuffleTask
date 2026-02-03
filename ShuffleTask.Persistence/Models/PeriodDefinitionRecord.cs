using SQLite;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Persistence.Models;

[Table("PeriodDefinition")]
internal sealed class PeriodDefinitionRecord
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public Weekdays Weekdays { get; set; }

    public TimeSpan? StartTime { get; set; }

    public TimeSpan? EndTime { get; set; }

    public bool IsAllDay { get; set; }

    public PeriodDefinitionMode Mode { get; set; }

    private static bool IsSettingsAligned(PeriodDefinitionMode mode)
    {
        return mode.HasFlag(PeriodDefinitionMode.AlignWithWorkHours)
            || mode.HasFlag(PeriodDefinitionMode.OffWorkRelativeToWorkHours)
            || mode.HasFlag(PeriodDefinitionMode.Morning)
            || mode.HasFlag(PeriodDefinitionMode.Lunch)
            || mode.HasFlag(PeriodDefinitionMode.Evening);
    }

    public static PeriodDefinitionRecord FromDomain(PeriodDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new PeriodDefinitionRecord
        {
            Id = definition.Id,
            Name = definition.Name,
            Weekdays = definition.Weekdays,
            StartTime = IsSettingsAligned(definition.Mode) ? null : definition.StartTime,
            EndTime = IsSettingsAligned(definition.Mode) ? null : definition.EndTime,
            IsAllDay = definition.IsAllDay,
            Mode = definition.Mode
        };
    }

    public PeriodDefinition ToDomain()
    {
        PeriodDefinitionMode mode = Mode;

        if (mode == PeriodDefinitionMode.None)
        {
            if (string.Equals(Id, PeriodDefinitionCatalog.WorkId, StringComparison.OrdinalIgnoreCase))
            {
                mode = PeriodDefinitionMode.AlignWithWorkHours;
            }
            else if (string.Equals(Id, PeriodDefinitionCatalog.OffWorkId, StringComparison.OrdinalIgnoreCase))
            {
                mode = PeriodDefinitionMode.AlignWithWorkHours | PeriodDefinitionMode.OffWorkRelativeToWorkHours;
            }
            else if (string.Equals(Id, PeriodDefinitionCatalog.MorningsId, StringComparison.OrdinalIgnoreCase))
            {
                mode = PeriodDefinitionMode.Morning;
            }
            else if (string.Equals(Id, PeriodDefinitionCatalog.LunchBreakId, StringComparison.OrdinalIgnoreCase))
            {
                mode = PeriodDefinitionMode.Lunch;
            }
            else if (string.Equals(Id, PeriodDefinitionCatalog.EveningsId, StringComparison.OrdinalIgnoreCase))
            {
                mode = PeriodDefinitionMode.Evening;
            }
        }

        return new PeriodDefinition
        {
            Id = Id,
            Name = Name,
            Weekdays = Weekdays,
            StartTime = IsSettingsAligned(mode) ? null : StartTime,
            EndTime = IsSettingsAligned(mode) ? null : EndTime,
            IsAllDay = IsAllDay,
            Mode = mode
        };
    }
}
