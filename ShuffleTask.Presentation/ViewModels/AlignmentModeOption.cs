using ShuffleTask.Domain.Entities;

namespace ShuffleTask.ViewModels;

public sealed class AlignmentModeOption
{
    public AlignmentModeOption(string name, string description, PeriodDefinitionMode mode)
    {
        Name = name;
        Description = description;
        Mode = mode;
    }

    public string Name { get; }

    public string Description { get; }

    public PeriodDefinitionMode Mode { get; }

    public static IReadOnlyList<AlignmentModeOption> CreateDefaults()
    {
        return new[]
        {
            new AlignmentModeOption(
                "Fixed time window",
                "Use the start and end times below.",
                PeriodDefinitionMode.None),
            new AlignmentModeOption(
                "Align with work hours",
                "Uses Settings → Work hours for the time range.",
                PeriodDefinitionMode.AlignWithWorkHours),
            new AlignmentModeOption(
                "Align with off-work hours",
                "Schedules outside Settings → Work hours (includes weekends).",
                PeriodDefinitionMode.AlignWithWorkHours | PeriodDefinitionMode.OffWorkRelativeToWorkHours)
        };
    }
}
