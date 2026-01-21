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

    public static PeriodDefinitionRecord FromDomain(PeriodDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new PeriodDefinitionRecord
        {
            Id = definition.Id,
            Name = definition.Name,
            Weekdays = definition.Weekdays,
            StartTime = definition.StartTime,
            EndTime = definition.EndTime,
            IsAllDay = definition.IsAllDay,
            Mode = definition.Mode
        };
    }

    public PeriodDefinition ToDomain()
    {
        return new PeriodDefinition
        {
            Id = Id,
            Name = Name,
            Weekdays = Weekdays,
            StartTime = StartTime,
            EndTime = EndTime,
            IsAllDay = IsAllDay,
            Mode = Mode
        };
    }
}
