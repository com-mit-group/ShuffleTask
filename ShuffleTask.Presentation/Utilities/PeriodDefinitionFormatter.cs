using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Presentation.Utilities;

internal static class PeriodDefinitionFormatter
{
    public static string FormatAllowedPeriodLabel(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (PeriodDefinitionCatalog.TryGet(task.PeriodDefinitionId, out PeriodDefinition definition))
        {
            return FormatDefinitionLabel(definition);
        }

        if (!string.IsNullOrWhiteSpace(task.PeriodDefinitionId)
            && TryBuildAdHocDefinition(task, out PeriodDefinition presetDefinition))
        {
            return $"Preset ({DescribeDefinition(presetDefinition)})";
        }

        if (TryBuildAdHocDefinition(task, out PeriodDefinition adHocDefinition))
        {
            return $"Ad-hoc ({DescribeDefinition(adHocDefinition)})";
        }

        return task.AllowedPeriod switch
        {
            AllowedPeriod.Work => "Work hours (Mon–Fri)",
            AllowedPeriod.OffWork => "Off hours (includes weekends)",
            AllowedPeriod.Custom => FormatLegacyCustom(task),
            _ => "Any time"
        };
    }

    public static string DescribeDefinition(PeriodDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        string weekdays = FormatWeekdays(definition.Weekdays);
        string timeWindow = FormatTimeWindow(definition);
        string alignment = FormatAlignment(definition.Mode);

        if (!string.IsNullOrWhiteSpace(alignment))
        {
            return $"{weekdays}, {timeWindow}. {alignment}";
        }

        return $"{weekdays}, {timeWindow}.";
    }

    private static string FormatDefinitionLabel(PeriodDefinition definition)
    {
        if (string.Equals(definition.Id, PeriodDefinitionCatalog.AnyId, StringComparison.OrdinalIgnoreCase))
        {
            return "Any time";
        }

        if (string.Equals(definition.Id, PeriodDefinitionCatalog.WorkId, StringComparison.OrdinalIgnoreCase))
        {
            return "Work hours";
        }

        if (string.Equals(definition.Id, PeriodDefinitionCatalog.OffWorkId, StringComparison.OrdinalIgnoreCase))
        {
            return "Off hours";
        }

        return definition.Name;
    }

    private static string FormatWeekdays(Weekdays weekdays)
    {
        if (weekdays == Weekdays.None)
        {
            return "Any day";
        }

        if (weekdays == PeriodDefinitionCatalog.AllWeekdays)
        {
            return "Every day";
        }

        if (weekdays == (Weekdays.Mon | Weekdays.Tue | Weekdays.Wed | Weekdays.Thu | Weekdays.Fri))
        {
            return "Weekdays";
        }

        if (weekdays == (Weekdays.Sat | Weekdays.Sun))
        {
            return "Weekends";
        }

        var labels = new List<string>();

        void Add(Weekdays day, string label)
        {
            if (weekdays.HasFlag(day))
            {
                labels.Add(label);
            }
        }

        Add(Weekdays.Mon, "Mon");
        Add(Weekdays.Tue, "Tue");
        Add(Weekdays.Wed, "Wed");
        Add(Weekdays.Thu, "Thu");
        Add(Weekdays.Fri, "Fri");
        Add(Weekdays.Sat, "Sat");
        Add(Weekdays.Sun, "Sun");

        return string.Join(", ", labels);
    }

    private static string FormatTimeWindow(PeriodDefinition definition)
    {
        if (definition.Mode.HasFlag(PeriodDefinitionMode.OffWorkRelativeToWorkHours))
        {
            return "Off-work hours";
        }

        if (definition.Mode.HasFlag(PeriodDefinitionMode.AlignWithWorkHours))
        {
            return "Work hours";
        }

        if (definition.IsAllDay)
        {
            return "All day";
        }

        if (definition.StartTime.HasValue && definition.EndTime.HasValue)
        {
            return $"{definition.StartTime:hh\\:mm}–{definition.EndTime:hh\\:mm}";
        }

        return "Custom hours";
    }

    private static string FormatAlignment(PeriodDefinitionMode mode)
    {
        if (mode.HasFlag(PeriodDefinitionMode.OffWorkRelativeToWorkHours))
        {
            return "Aligns to off-work time based on Settings → Work hours (includes weekends).";
        }

        if (mode.HasFlag(PeriodDefinitionMode.AlignWithWorkHours))
        {
            return "Aligns to Settings → Work hours.";
        }

        return string.Empty;
    }

    private static bool TryBuildAdHocDefinition(TaskItem task, out PeriodDefinition definition)
    {
        bool hasAdHocDefinition = task.AdHocStartTime.HasValue
            || task.AdHocEndTime.HasValue
            || task.AdHocWeekdays.HasValue
            || task.AdHocIsAllDay
            || task.AdHocMode != PeriodDefinitionMode.None;

        if (!hasAdHocDefinition)
        {
            definition = PeriodDefinitionCatalog.Any;
            return false;
        }

        definition = new PeriodDefinition
        {
            Id = string.Empty,
            Name = "Ad-hoc",
            StartTime = task.AdHocStartTime,
            EndTime = task.AdHocEndTime,
            Weekdays = task.AdHocWeekdays ?? PeriodDefinitionCatalog.AllWeekdays,
            IsAllDay = task.AdHocIsAllDay,
            Mode = task.AdHocMode
        };
        return true;
    }

    private static string FormatLegacyCustom(TaskItem task)
    {
        if (task.CustomStartTime.HasValue && task.CustomEndTime.HasValue)
        {
            return $"Custom hours ({task.CustomStartTime:hh\\:mm}–{task.CustomEndTime:hh\\:mm})";
        }

        return "Custom hours";
    }
}
