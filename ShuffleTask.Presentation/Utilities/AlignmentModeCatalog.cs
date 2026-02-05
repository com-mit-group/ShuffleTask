using ShuffleTask.Domain.Entities;
using ShuffleTask.ViewModels;

namespace ShuffleTask.Presentation.Utilities;

public static class AlignmentModeCatalog
{
    public static IReadOnlyList<AlignmentModeOption> Defaults { get; } =
        new[]
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
                PeriodDefinitionMode.AlignWithWorkHours | PeriodDefinitionMode.OffWorkRelativeToWorkHours),
            new AlignmentModeOption(
                "Morning (settings)",
                "Uses Settings → Morning hours for the time range.",
                PeriodDefinitionMode.Morning),
            new AlignmentModeOption(
                "Lunch (settings)",
                "Uses Settings → Lunch hours for the time range.",
                PeriodDefinitionMode.Lunch),
            new AlignmentModeOption(
                "Evening (settings)",
                "Uses Settings → Evening hours for the time range.",
                PeriodDefinitionMode.Evening)
        };
}
