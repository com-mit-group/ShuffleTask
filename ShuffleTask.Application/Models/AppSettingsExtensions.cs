namespace ShuffleTask.Application.Models;

public static class AppSettingsExtensions
{
    public static void CopyFrom(this AppSettings target, AppSettings source)
    {
        if (target == null || source == null)
        {
            return;
        }

        target.WorkStart = source.WorkStart;
        target.WorkEnd = source.WorkEnd;
        target.TimerMode = source.TimerMode;
        target.FocusMinutes = source.FocusMinutes;
        target.BreakMinutes = source.BreakMinutes;
        target.PomodoroCycles = source.PomodoroCycles;
        target.MinGapMinutes = source.MinGapMinutes;
        target.MaxGapMinutes = source.MaxGapMinutes;
        target.ReminderMinutes = source.ReminderMinutes;
        target.EnableNotifications = source.EnableNotifications;
        target.SoundOn = source.SoundOn;
        target.Active = source.Active;
        target.AutoShuffleEnabled = source.AutoShuffleEnabled;
        target.ManualShuffleRespectsAllowedPeriod = source.ManualShuffleRespectsAllowedPeriod;
        target.MaxDailyShuffles = source.MaxDailyShuffles;
        target.QuietHoursStart = source.QuietHoursStart;
        target.QuietHoursEnd = source.QuietHoursEnd;
        target.StreakBias = source.StreakBias;
        target.StableRandomnessPerDay = source.StableRandomnessPerDay;
        target.ImportanceWeight = source.ImportanceWeight;
        target.UrgencyWeight = source.UrgencyWeight;
        target.UrgencyDeadlineShare = source.UrgencyDeadlineShare;
        target.RepeatUrgencyPenalty = source.RepeatUrgencyPenalty;
        target.SizeBiasStrength = source.SizeBiasStrength;
        target.Network = source.Network;
    }
}
