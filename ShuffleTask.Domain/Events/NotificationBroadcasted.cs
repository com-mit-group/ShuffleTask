using System.Text.Json;
using System.Text.Json.Serialization;
using Yaref92.Events;

namespace ShuffleTask.Domain.Events;

[JsonConverter(typeof(NotificationBroadcastedConverter))]
public sealed class NotificationBroadcasted : DomainEventBase
{
    public NotificationBroadcasted(
        NotificationIdentity identity,
        NotificationContent content,
        NotificationSchedule schedule,
        bool isReminder,
        DateTime? occuredAt = null,
        Guid? eventId = null)
        : base(occuredAt ?? default, eventId ?? Guid.Empty)
    {
        if (string.IsNullOrWhiteSpace(identity.NotificationId))
        {
            throw new ArgumentException("Notification id must be provided.", nameof(identity));
        }

        if (string.IsNullOrWhiteSpace(content.Title))
        {
            throw new ArgumentException("Notification title must be provided.", nameof(content));
        }

        if (string.IsNullOrWhiteSpace(content.Message))
        {
            throw new ArgumentException("Notification message must be provided.", nameof(content));
        }

        if (string.IsNullOrWhiteSpace(identity.DeviceId))
        {
            throw new ArgumentException("Device id must be provided.", nameof(identity));
        }

        NotificationId = identity.NotificationId;
        Title = content.Title;
        Message = content.Message;
        DeviceId = identity.DeviceId;
        TaskId = schedule.TaskId;
        ScheduledUtc = EnsureUtc(schedule.ScheduledUtc);
        Delay = schedule.Delay;
        IsReminder = isReminder;
    }

    public string NotificationId { get; }

    public string Title { get; }

    public string Message { get; }

    public string DeviceId { get; }

    public string? TaskId { get; }

    public DateTime ScheduledUtc { get; }

    public TimeSpan? Delay { get; }

    public bool IsReminder { get; }



    public sealed record NotificationIdentity(string NotificationId, string DeviceId);

    public sealed record NotificationContent(string Title, string Message);

    public sealed record NotificationSchedule(string? TaskId, DateTime ScheduledUtc, TimeSpan? Delay);

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);
    }

    private sealed class NotificationBroadcastedConverter : JsonConverter<NotificationBroadcasted>
    {
        public override NotificationBroadcasted? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected start of object while deserializing NotificationBroadcasted.");
            }

            string? notificationId = null;
            string? title = null;
            string? message = null;
            string? deviceId = null;
            string? taskId = null;
            DateTime? scheduledUtc = null;
            TimeSpan? delay = null;
            bool? isReminder = null;
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
                    throw new JsonException("Unexpected token while reading NotificationBroadcasted.");
                }

                string propertyName = reader.GetString() ?? string.Empty;
                reader.Read();

                switch (propertyName)
                {
                    case "notificationId":
                        notificationId = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    case "title":
                        title = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    case "message":
                        message = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    case "deviceId":
                        deviceId = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    case "taskId":
                        taskId = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    case "scheduledUtc":
                        scheduledUtc = JsonSerializer.Deserialize<DateTime>(ref reader, options);
                        break;
                    case "delay":
                        delay = JsonSerializer.Deserialize<TimeSpan?>(ref reader, options);
                        break;
                    case "isReminder":
                        isReminder = reader.TokenType == JsonTokenType.Null ? null : reader.GetBoolean();
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

            if (string.IsNullOrWhiteSpace(notificationId))
            {
                throw new JsonException("notificationId is required for NotificationBroadcasted.");
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                throw new JsonException("title is required for NotificationBroadcasted.");
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                throw new JsonException("message is required for NotificationBroadcasted.");
            }

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                throw new JsonException("deviceId is required for NotificationBroadcasted.");
            }

            if (scheduledUtc is null)
            {
                throw new JsonException("scheduledUtc is required for NotificationBroadcasted.");
            }

            if (isReminder is null)
            {
                throw new JsonException("isReminder is required for NotificationBroadcasted.");
            }

            var identity = new NotificationIdentity(notificationId, deviceId);
            var content = new NotificationContent(title, message);
            var schedule = new NotificationSchedule(taskId, scheduledUtc.Value, delay);

            return new NotificationBroadcasted(identity, content, schedule, isReminder.Value, occuredAt, eventId);
        }

        public override void Write(Utf8JsonWriter writer, NotificationBroadcasted value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("notificationId", value.NotificationId);
            writer.WriteString("title", value.Title);
            writer.WriteString("message", value.Message);
            writer.WriteString("deviceId", value.DeviceId);

            if (value.TaskId is not null)
            {
                writer.WriteString("taskId", value.TaskId);
            }
            else
            {
                writer.WriteNull("taskId");
            }

            writer.WritePropertyName("scheduledUtc");
            JsonSerializer.Serialize(writer, value.ScheduledUtc, options);

            writer.WritePropertyName("delay");
            JsonSerializer.Serialize(writer, value.Delay, options);

            writer.WriteBoolean("isReminder", value.IsReminder);

            writer.WritePropertyName("occuredAt");
            JsonSerializer.Serialize(writer, value.DateTimeOccurredUtc, options);

            writer.WritePropertyName("eventId");
            JsonSerializer.Serialize(writer, value.EventId, options);

            writer.WriteEndObject();
        }
    }
}
