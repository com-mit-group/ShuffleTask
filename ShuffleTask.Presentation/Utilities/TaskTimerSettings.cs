using System;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Presentation.Utilities;

internal readonly record struct EffectiveTimerSettings(
    TimerMode Mode,
    int ReminderMinutes,
    int FocusMinutes,
    int BreakMinutes,
    int PomodoroCycles)
{
    public int InitialMinutes => Mode == TimerMode.Pomodoro ? FocusMinutes : ReminderMinutes;
}

internal static class TaskTimerSettings
{
    public static EffectiveTimerSettings Resolve(TaskItem task, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(settings);

        TimerMode mode = task.CustomTimerMode.HasValue
            ? (TimerMode)task.CustomTimerMode.Value
            : settings.TimerMode;

        int reminderMinutes = task.CustomReminderMinutes ?? settings.ReminderMinutes;
        int focusMinutes = task.CustomFocusMinutes ?? settings.FocusMinutes;
        int breakMinutes = task.CustomBreakMinutes ?? settings.BreakMinutes;
        int pomodoroCycles = task.CustomPomodoroCycles ?? settings.PomodoroCycles;

        return new EffectiveTimerSettings(mode, reminderMinutes, focusMinutes, breakMinutes, pomodoroCycles);
    }
}
