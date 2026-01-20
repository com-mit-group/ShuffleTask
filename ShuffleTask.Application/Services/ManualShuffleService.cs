using System;
using System.Collections.Generic;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Services;

public static class ManualShuffleService
{
    public static List<TaskItem> CreateCandidatePool(IEnumerable<TaskItem> tasks, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(settings);

        var clones = new List<TaskItem>();

        foreach (var task in tasks)
        {
            if (task == null)
            {
                continue;
            }

            var clone = TaskItem.Clone(task);
            clone.AutoShuffleAllowed = true;

            if (!settings.ManualShuffleRespectsAllowedPeriod)
            {
                clone.AllowedPeriod = AllowedPeriod.Any;
                clone.PeriodDefinitionId = PeriodDefinitionCatalog.AnyId;
                clone.AdHocStartTime = null;
                clone.AdHocEndTime = null;
                clone.AdHocWeekdays = null;
                clone.AdHocIsAllDay = false;
                clone.AdHocMode = PeriodDefinitionMode.None;
                clone.CustomStartTime = null;
                clone.CustomEndTime = null;
                clone.CustomWeekdays = null;
            }

            clones.Add(clone);
        }

        return clones;
    }
}
