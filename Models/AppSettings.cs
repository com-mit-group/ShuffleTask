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
}
