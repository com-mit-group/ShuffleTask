using System.Text.Json;
using System.Text.Json.Serialization;
using Yaref92.Events;

namespace ShuffleTask.Domain.Events;

[JsonConverter(typeof(ShuffleStateChangedConverter))]
public sealed class ShuffleStateChanged : DomainEventBase
{
    public ShuffleStateChanged(
        ShuffleDeviceContext context,
        ShuffleTimerSnapshot timer,
        DateTime? occuredAt = null,
        Guid? eventId = null)
        : base(occuredAt ?? default, eventId ?? Guid.Empty)
    {
        if (string.IsNullOrWhiteSpace(context.DeviceId))
        {
            throw new ArgumentException("Device id must be provided.", nameof(context));
        }

        DeviceId = context.DeviceId;
        TaskId = context.TaskId;
        IsAutoShuffle = context.IsAutoShuffle;
        Trigger = context.Trigger;
        EventTimestampUtc = EnsureUtc(context.EventTimestampUtc);
        TimerDurationSeconds = timer.TimerDurationSeconds;
        TimerExpiresUtc = timer.TimerExpiresUtc.HasValue ? EnsureUtc(timer.TimerExpiresUtc.Value) : null;
        TimerMode = timer.TimerMode;
        PomodoroPhase = timer.PomodoroPhase;
        PomodoroCycleIndex = timer.PomodoroCycleIndex;
        PomodoroCycleCount = timer.PomodoroCycleCount;
        FocusMinutes = timer.FocusMinutes;
        BreakMinutes = timer.BreakMinutes;
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
        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);
    }

    private sealed class ShuffleStateChangedConverter : JsonConverter<ShuffleStateChanged>
    {
        public override ShuffleStateChanged? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var dto = JsonSerializer.Deserialize<LegacyShuffleStateChangedDto>(ref reader, options)
                ?? throw new JsonException("Expected state payload when deserializing ShuffleStateChanged.");

            var context = new ShuffleDeviceContext(
                RequireString(dto.DeviceId, "deviceId"),
                dto.TaskId,
                RequireBoolean(dto.IsAutoShuffle, "isAutoShuffle"),
                RequireString(dto.Trigger, "trigger"),
                RequireDateTime(dto.EventTimestampUtc, "eventTimestampUtc"));

            var snapshot = new ShuffleTimerSnapshot(
                dto.TimerDurationSeconds,
                dto.TimerExpiresUtc,
                dto.TimerMode,
                dto.PomodoroPhase,
                dto.PomodoroCycleIndex,
                dto.PomodoroCycleCount,
                dto.FocusMinutes,
                dto.BreakMinutes);

            return new ShuffleStateChanged(context, snapshot, dto.OccuredAt, dto.EventId);
        }

        public override void Write(Utf8JsonWriter writer, ShuffleStateChanged value, JsonSerializerOptions options)
        {
            var dto = new LegacyShuffleStateChangedDto
            {
                DeviceId = value.DeviceId,
                TaskId = value.TaskId,
                IsAutoShuffle = value.IsAutoShuffle,
                Trigger = value.Trigger,
                EventTimestampUtc = value.EventTimestampUtc,
                TimerDurationSeconds = value.TimerDurationSeconds,
                TimerExpiresUtc = value.TimerExpiresUtc,
                TimerMode = value.TimerMode,
                PomodoroPhase = value.PomodoroPhase,
                PomodoroCycleIndex = value.PomodoroCycleIndex,
                PomodoroCycleCount = value.PomodoroCycleCount,
                FocusMinutes = value.FocusMinutes,
                BreakMinutes = value.BreakMinutes,
                OccuredAt = value.OccuredAt,
                EventId = value.EventId,
            };

            JsonSerializer.Serialize(writer, dto, options);
        }

        private static string RequireString(string? value, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new JsonException($"{propertyName} is required for ShuffleStateChanged.");
            }

            return value;
        }

        private static bool RequireBoolean(bool? value, string propertyName)
        {
            if (value is null)
            {
                throw new JsonException($"{propertyName} is required for ShuffleStateChanged.");
            }

            return value.Value;
        }

        private static DateTime RequireDateTime(DateTime? value, string propertyName)
        {
            if (value is null)
            {
                throw new JsonException($"{propertyName} is required for ShuffleStateChanged.");
            }

            return value.Value;
        }

        private sealed class LegacyShuffleStateChangedDto
        {
            [JsonPropertyName("deviceId")]
            public string? DeviceId { get; set; }

            [JsonPropertyName("taskId")]
            public string? TaskId { get; set; }

            [JsonPropertyName("isAutoShuffle")]
            public bool? IsAutoShuffle { get; set; }

            [JsonPropertyName("trigger")]
            public string? Trigger { get; set; }

            [JsonPropertyName("eventTimestampUtc")]
            public DateTime? EventTimestampUtc { get; set; }

            [JsonPropertyName("timerDurationSeconds")]
            public int? TimerDurationSeconds { get; set; }

            [JsonPropertyName("timerExpiresUtc")]
            public DateTime? TimerExpiresUtc { get; set; }

            [JsonPropertyName("timerMode")]
            public int? TimerMode { get; set; }

            [JsonPropertyName("pomodoroPhase")]
            public int? PomodoroPhase { get; set; }

            [JsonPropertyName("pomodoroCycleIndex")]
            public int? PomodoroCycleIndex { get; set; }

            [JsonPropertyName("pomodoroCycleCount")]
            public int? PomodoroCycleCount { get; set; }

            [JsonPropertyName("focusMinutes")]
            public int? FocusMinutes { get; set; }

            [JsonPropertyName("breakMinutes")]
            public int? BreakMinutes { get; set; }

            [JsonPropertyName("occuredAt")]
            public DateTime? OccuredAt { get; set; }

            [JsonPropertyName("eventId")]
            public Guid? EventId { get; set; }
        }
    }
}
