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
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected start of object while deserializing ShuffleStateChanged.");
            }

            string? deviceId = null;
            string? taskId = null;
            bool? isAutoShuffle = null;
            string? trigger = null;
            DateTime? eventTimestampUtc = null;
            int? timerDurationSeconds = null;
            DateTime? timerExpiresUtc = null;
            int? timerMode = null;
            int? pomodoroPhase = null;
            int? pomodoroCycleIndex = null;
            int? pomodoroCycleCount = null;
            int? focusMinutes = null;
            int? breakMinutes = null;
            DateTime? occuredAt = null;
            Guid? eventId = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Unexpected token while reading ShuffleStateChanged.");
                }

                string propertyName = reader.GetString() ?? string.Empty;
                reader.Read();

                switch (propertyName)
                {
                    case "deviceId":
                        deviceId = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    case "taskId":
                        taskId = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    case "isAutoShuffle":
                        isAutoShuffle = reader.TokenType == JsonTokenType.Null ? null : reader.GetBoolean();
                        break;
                    case "trigger":
                        trigger = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    case "eventTimestampUtc":
                        eventTimestampUtc = JsonSerializer.Deserialize<DateTime>(ref reader, options);
                        break;
                    case "timerDurationSeconds":
                        timerDurationSeconds = reader.TokenType == JsonTokenType.Null
                            ? null
                            : JsonSerializer.Deserialize<int?>(ref reader, options);
                        break;
                    case "timerExpiresUtc":
                        timerExpiresUtc = JsonSerializer.Deserialize<DateTime?>(ref reader, options);
                        break;
                    case "timerMode":
                        timerMode = reader.TokenType == JsonTokenType.Null
                            ? null
                            : JsonSerializer.Deserialize<int?>(ref reader, options);
                        break;
                    case "pomodoroPhase":
                        pomodoroPhase = reader.TokenType == JsonTokenType.Null
                            ? null
                            : JsonSerializer.Deserialize<int?>(ref reader, options);
                        break;
                    case "pomodoroCycleIndex":
                        pomodoroCycleIndex = reader.TokenType == JsonTokenType.Null
                            ? null
                            : JsonSerializer.Deserialize<int?>(ref reader, options);
                        break;
                    case "pomodoroCycleCount":
                        pomodoroCycleCount = reader.TokenType == JsonTokenType.Null
                            ? null
                            : JsonSerializer.Deserialize<int?>(ref reader, options);
                        break;
                    case "focusMinutes":
                        focusMinutes = reader.TokenType == JsonTokenType.Null
                            ? null
                            : JsonSerializer.Deserialize<int?>(ref reader, options);
                        break;
                    case "breakMinutes":
                        breakMinutes = reader.TokenType == JsonTokenType.Null
                            ? null
                            : JsonSerializer.Deserialize<int?>(ref reader, options);
                        break;
                    case "occuredAt":
                        occuredAt = reader.TokenType == JsonTokenType.Null
                            ? null
                            : JsonSerializer.Deserialize<DateTime?>(ref reader, options);
                        break;
                    case "eventId":
                        eventId = reader.TokenType == JsonTokenType.Null
                            ? null
                            : JsonSerializer.Deserialize<Guid?>(ref reader, options);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                throw new JsonException("deviceId is required for ShuffleStateChanged.");
            }

            if (isAutoShuffle is null)
            {
                throw new JsonException("isAutoShuffle is required for ShuffleStateChanged.");
            }

            if (trigger is null)
            {
                throw new JsonException("trigger is required for ShuffleStateChanged.");
            }

            if (eventTimestampUtc is null)
            {
                throw new JsonException("eventTimestampUtc is required for ShuffleStateChanged.");
            }

            var context = new ShuffleDeviceContext(deviceId, taskId, isAutoShuffle.Value, trigger, eventTimestampUtc.Value);
            var snapshot = new ShuffleTimerSnapshot(
                timerDurationSeconds,
                timerExpiresUtc,
                timerMode,
                pomodoroPhase,
                pomodoroCycleIndex,
                pomodoroCycleCount,
                focusMinutes,
                breakMinutes);

            return new ShuffleStateChanged(context, snapshot, occuredAt, eventId);
        }

        public override void Write(Utf8JsonWriter writer, ShuffleStateChanged value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("deviceId", value.DeviceId);

            if (value.TaskId is not null)
            {
                writer.WriteString("taskId", value.TaskId);
            }
            else
            {
                writer.WriteNull("taskId");
            }

            writer.WriteBoolean("isAutoShuffle", value.IsAutoShuffle);
            writer.WriteString("trigger", value.Trigger);

            writer.WritePropertyName("eventTimestampUtc");
            JsonSerializer.Serialize(writer, value.EventTimestampUtc, options);

            writer.WritePropertyName("timerDurationSeconds");
            JsonSerializer.Serialize(writer, value.TimerDurationSeconds, options);

            writer.WritePropertyName("timerExpiresUtc");
            JsonSerializer.Serialize(writer, value.TimerExpiresUtc, options);

            writer.WritePropertyName("timerMode");
            JsonSerializer.Serialize(writer, value.TimerMode, options);

            writer.WritePropertyName("pomodoroPhase");
            JsonSerializer.Serialize(writer, value.PomodoroPhase, options);

            writer.WritePropertyName("pomodoroCycleIndex");
            JsonSerializer.Serialize(writer, value.PomodoroCycleIndex, options);

            writer.WritePropertyName("pomodoroCycleCount");
            JsonSerializer.Serialize(writer, value.PomodoroCycleCount, options);

            writer.WritePropertyName("focusMinutes");
            JsonSerializer.Serialize(writer, value.FocusMinutes, options);

            writer.WritePropertyName("breakMinutes");
            JsonSerializer.Serialize(writer, value.BreakMinutes, options);

            writer.WritePropertyName("occuredAt");
            JsonSerializer.Serialize(writer, value.OccuredAt, options);

            writer.WritePropertyName("eventId");
            JsonSerializer.Serialize(writer, value.EventId, options);

            writer.WriteEndObject();
        }
    }
}
