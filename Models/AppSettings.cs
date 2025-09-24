namespace ShuffleTask.Models;

public class AppSettings
{
    public TimeSpan WorkStart { get; set; } = new TimeSpan(9, 0, 0); // default 09:00
    public TimeSpan WorkEnd { get; set; } = new TimeSpan(17, 0, 0); // default 17:00

    public int MinGapMinutes { get; set; } = 45; // default 45
    public int MaxGapMinutes { get; set; } = 150; // default 150

    public int ReminderMinutes { get; set; } = 60; // default 60 (one-hour timer)

    public bool EnableNotifications { get; set; } = true; // default true
    public bool SoundOn { get; set; } = true; // default true

    public bool Active { get; set; } = true; // master switch

    // New settings
    // 0..1, scales preference for tasks not done in a while
    public double StreakBias { get; set; } = 0.3;

    // When true (default), randomness is stable per day; when false, more chaotic
    public bool StableRandomnessPerDay { get; set; } = true;

    // Weighting controls
    // Importance vs urgency weighting expressed in points (default 60/40 split)
    public double ImportanceWeight { get; set; } = 60.0;
    public double UrgencyWeight { get; set; } = 40.0;

    // Percent of the urgency share that should go to deadlines (0-100)
    public double UrgencyDeadlineShare { get; set; } = 75.0;

    // Dampens how strongly repeating work counts toward urgency (0-1 by default)
    public double RepeatUrgencyPenalty { get; set; } = 0.6;

    // Strength of the size-based multiplier; 0 disables, higher values boost small work
    public double SizeBiasStrength { get; set; } = 0.2;
}
