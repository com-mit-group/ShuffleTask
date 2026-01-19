using System.Runtime.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using ShuffleTask.Application.Models;

namespace ShuffleTask.Application.Models;

public enum TimerMode
{
    LongInterval = 0,
    Pomodoro = 1
}

public partial class AppSettings : ObservableObject
{
    private const double ImportanceUrgencyTotal = 100.0;
    private const double DefaultImportanceWeight = 60.0;
    private const double DefaultUrgencyWeight = 40.0;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int EventVersion { get; set; }

    [ObservableProperty]
    private TimeSpan workStart = new(9, 0, 0); // default 09:00

    [ObservableProperty]
    private TimeSpan workEnd = new(17, 0, 0); // default 17:00
    public TimerMode TimerMode { get; set; } = TimerMode.LongInterval;

    public int FocusMinutes { get; set; } = 15;

    public int BreakMinutes { get; set; } = 5;

    public int PomodoroCycles { get; set; } = 3;

    [ObservableProperty]
    private int minGapMinutes = 45; // default 45

    [ObservableProperty]
    private int maxGapMinutes = 150; // default 150

    [ObservableProperty]
    private int reminderMinutes = 60; // default 60 (one-hour timer)

    [ObservableProperty]
    private bool enableNotifications = true; // default true

    [ObservableProperty]
    private bool soundOn = true; // default true

    [ObservableProperty]
    private bool active = true; // master switch

    [ObservableProperty]
    private bool backgroundActivityEnabled = true; // background scheduling + notifications

    public bool AutoShuffleEnabled { get; set; } = true;

    [ObservableProperty]
    private bool manualShuffleRespectsAllowedPeriod = true;

    public int MaxDailyShuffles { get; set; } = 6;

    public TimeSpan QuietHoursStart { get; set; } = new TimeSpan(22, 0, 0);

    public TimeSpan QuietHoursEnd { get; set; } = new TimeSpan(7, 0, 0);

    // New settings
    // 0..1, scales preference for tasks not done in a while
    [ObservableProperty]
    private double streakBias = 0.3;

    // When true (default), randomness is stable per day; when false, more chaotic
    [ObservableProperty]
    private bool stableRandomnessPerDay = true;

    private double importanceWeight = DefaultImportanceWeight;
    private double urgencyWeight = DefaultUrgencyWeight;

    // Percent of the urgency share that should go to deadlines (0-100)
    [ObservableProperty]
    private double urgencyDeadlineShare = 75.0;

    // Dampens how strongly repeating work counts toward urgency (0-1 by default)
    [ObservableProperty]
    private double repeatUrgencyPenalty = 0.6;

    // Strength of the size-based multiplier; 0 disables, higher values boost large work
    [ObservableProperty]
    private double sizeBiasStrength = 0.2;

    [ObservableProperty]
    private NetworkOptions network;

    public AppSettings()
    {
        this.network = NetworkOptions.CreateDefault();
        NormalizeWeights();
    }

    public double ImportanceWeight
    {
        get => importanceWeight;
        set => UpdateImportanceAndUrgency(value, null);
    }

    public double UrgencyWeight
    {
        get => urgencyWeight;
        set => UpdateImportanceAndUrgency(null, value);
    }

    public void CopyFrom(AppSettings? source)
    {
        if (source is null)
        {
            return;
        }

        WorkStart = source.WorkStart;
        WorkEnd = source.WorkEnd;
        TimerMode = source.TimerMode;
        FocusMinutes = source.FocusMinutes;
        BreakMinutes = source.BreakMinutes;
        PomodoroCycles = source.PomodoroCycles;
        MinGapMinutes = source.MinGapMinutes;
        MaxGapMinutes = source.MaxGapMinutes;
        ReminderMinutes = source.ReminderMinutes;
        EnableNotifications = source.EnableNotifications;
        SoundOn = source.SoundOn;
        Active = source.Active;
        BackgroundActivityEnabled = source.BackgroundActivityEnabled;
        AutoShuffleEnabled = source.AutoShuffleEnabled;
        ManualShuffleRespectsAllowedPeriod = source.ManualShuffleRespectsAllowedPeriod;
        MaxDailyShuffles = source.MaxDailyShuffles;
        QuietHoursStart = source.QuietHoursStart;
        QuietHoursEnd = source.QuietHoursEnd;
        StreakBias = source.StreakBias;
        StableRandomnessPerDay = source.StableRandomnessPerDay;
        ImportanceWeight = source.ImportanceWeight;
        UrgencyWeight = source.UrgencyWeight;
        UrgencyDeadlineShare = source.UrgencyDeadlineShare;
        RepeatUrgencyPenalty = source.RepeatUrgencyPenalty;
        SizeBiasStrength = source.SizeBiasStrength;
        Network = source.Network;
        UpdatedAt = source.UpdatedAt;
        EventVersion = source.EventVersion;
    }

    public void Touch(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        UpdatedAt = clock.GetUtcNow().UtcDateTime;
        EventVersion = Math.Max(1, EventVersion + 1);
    }

    public void NormalizeWeights()
    {
        double sum = importanceWeight + urgencyWeight;

        if (sum <= 0)
        {
            UpdateImportanceAndUrgency(DefaultImportanceWeight, null);
            return;
        }

        if (Math.Abs(sum - ImportanceUrgencyTotal) > 0.0001)
        {
            double scale = ImportanceUrgencyTotal / sum;
            UpdateImportanceAndUrgency(importanceWeight * scale, null);
        }
        else
        {
            UpdateImportanceAndUrgency(importanceWeight, null);
        }
    }

    private void UpdateImportanceAndUrgency(double? newImportance, double? newUrgency)
    {
        double importanceShare;
        double urgencyShare;

        if (newImportance.HasValue)
        {
            importanceShare = Math.Clamp(newImportance.Value, 0.0, ImportanceUrgencyTotal);
        }
        else if (newUrgency.HasValue)
        {
            urgencyShare = Math.Clamp(newUrgency.Value, 0.0, ImportanceUrgencyTotal);
            importanceShare = ImportanceUrgencyTotal - urgencyShare;
        }
        else
        {
            importanceShare = Math.Clamp(importanceWeight, 0.0, ImportanceUrgencyTotal);
        }

        urgencyShare = ImportanceUrgencyTotal - importanceShare;

        SetProperty(ref importanceWeight, importanceShare, nameof(ImportanceWeight));
        SetProperty(ref urgencyWeight, urgencyShare, nameof(UrgencyWeight));
    }

    [OnDeserialized]
    private void OnDeserialized(StreamingContext context)
    {
        NormalizeWeights();
        UpdatedAt = EnsureUtc(UpdatedAt == default ? DateTime.UtcNow : UpdatedAt);
        EventVersion = Math.Max(1, EventVersion);
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
