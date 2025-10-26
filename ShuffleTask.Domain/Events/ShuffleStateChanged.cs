using System.Text.Json.Serialization;
using Yaref92.Events;

namespace ShuffleTask.Domain.Events;

public sealed class ShuffleStateChanged : DomainEventBase
{
    public ShuffleStateChanged(
        ShuffleDeviceContext context,
        ShuffleTimerSnapshot timer,
        DateTime? occuredAt = null,
        Guid? eventId = null)
        : this(
            context.DeviceId,
            context.TaskId,
            context.IsAutoShuffle,
            context.Trigger,
            context.EventTimestampUtc,
            timer.TimerDurationSeconds,
            timer.TimerExpiresUtc,
            timer.TimerMode,
            timer.PomodoroPhase,
            timer.PomodoroCycleIndex,
            timer.PomodoroCycleCount,
            timer.FocusMinutes,
            timer.BreakMinutes,
            occuredAt,
            eventId)
    {
    }

    [JsonConstructor]
    private ShuffleStateChanged(
        string deviceId,
        string? taskId,
        bool isAutoShuffle,
        string trigger,
        DateTime eventTimestampUtc,
        int? timerDurationSeconds,
        DateTime? timerExpiresUtc,
        int? timerMode,
        int? pomodoroPhase,
        int? pomodoroCycleIndex,
        int? pomodoroCycleCount,
        int? focusMinutes,
        int? breakMinutes,
        DateTime? occuredAt,
        Guid? eventId)
        : base(occuredAt ?? default, eventId ?? Guid.Empty)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ArgumentException("Device id must be provided.", nameof(deviceId));
        }

        DeviceId = deviceId;
        TaskId = taskId;
        IsAutoShuffle = isAutoShuffle;
        Trigger = trigger;
        EventTimestampUtc = EnsureUtc(eventTimestampUtc);
        TimerDurationSeconds = timerDurationSeconds;
        TimerExpiresUtc = timerExpiresUtc.HasValue ? EnsureUtc(timerExpiresUtc.Value) : null;
        TimerMode = timerMode;
        PomodoroPhase = pomodoroPhase;
        PomodoroCycleIndex = pomodoroCycleIndex;
        PomodoroCycleCount = pomodoroCycleCount;
        FocusMinutes = focusMinutes;
        BreakMinutes = breakMinutes;
    }

    public string DeviceId { get; }

    public string? TaskId { get; }

    public bool IsAutoShuffle { get; }

    public string Trigger { get; }

    public DateTime EventTimestampUtc { get; }

    public int? TimerDurationSeconds { get; }

    public DateTime? TimerExpiresUtc { get; }

    public int? TimerMode { get; }

    public int? PomodoroPhase { get; }

    public int? PomodoroCycleIndex { get; }

    public int? PomodoroCycleCount { get; }

    public int? FocusMinutes { get; }

    public int? BreakMinutes { get; }

    public sealed record ShuffleDeviceContext(
        string DeviceId,
        string? TaskId,
        bool IsAutoShuffle,
        string Trigger,
        DateTime EventTimestampUtc);

    public sealed record ShuffleTimerSnapshot(
        int? TimerDurationSeconds,
        DateTime? TimerExpiresUtc,
        int? TimerMode,
        int? PomodoroPhase,
        int? PomodoroCycleIndex,
        int? PomodoroCycleCount,
        int? FocusMinutes,
        int? BreakMinutes);

    public bool HasActiveTask => !string.IsNullOrWhiteSpace(TaskId);

    public bool TimerActive => TimerDurationSeconds.HasValue && TimerDurationSeconds > 0 && TimerExpiresUtc.HasValue;

    private static DateTime EnsureUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }

        return DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);
    }
}
