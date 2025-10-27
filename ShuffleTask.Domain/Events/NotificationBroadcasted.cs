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
            var dto = JsonSerializer.Deserialize<LegacyNotificationBroadcastedDto>(ref reader, options)
                ?? throw new JsonException("Expected notification payload when deserializing NotificationBroadcasted.");

            var identity = new NotificationIdentity(
                RequireString(dto.NotificationId, "notificationId"),
                RequireString(dto.DeviceId, "deviceId"));

            var content = new NotificationContent(
                RequireString(dto.Title, "title"),
                RequireString(dto.Message, "message"));

            var schedule = new NotificationSchedule(
                dto.TaskId,
                RequireDateTime(dto.ScheduledUtc, "scheduledUtc"),
                dto.Delay);

            return new NotificationBroadcasted(
                identity,
                content,
                schedule,
                RequireBoolean(dto.IsReminder, "isReminder"),
                dto.OccuredAt,
                dto.EventId);
        }

        public override void Write(Utf8JsonWriter writer, NotificationBroadcasted value, JsonSerializerOptions options)
        {
            var dto = new LegacyNotificationBroadcastedDto
            {
                NotificationId = value.NotificationId,
                Title = value.Title,
                Message = value.Message,
                DeviceId = value.DeviceId,
                TaskId = value.TaskId,
                ScheduledUtc = value.ScheduledUtc,
                Delay = value.Delay,
                IsReminder = value.IsReminder,
                OccuredAt = value.DateTimeOccurredUtc,
                EventId = value.EventId,
            };

            JsonSerializer.Serialize(writer, dto, options);
        }

        private static string RequireString(string? value, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new JsonException($"{propertyName} is required for NotificationBroadcasted.");
            }

            return value;
        }

        private static DateTime RequireDateTime(DateTime? value, string propertyName)
        {
            if (value is null)
            {
                throw new JsonException($"{propertyName} is required for NotificationBroadcasted.");
            }

            return value.Value;
        }

        private static bool RequireBoolean(bool? value, string propertyName)
        {
            if (value is null)
            {
                throw new JsonException($"{propertyName} is required for NotificationBroadcasted.");
            }

            return value.Value;
        }

        private sealed class LegacyNotificationBroadcastedDto
        {
            [JsonPropertyName("notificationId")]
            public string? NotificationId { get; set; }

            [JsonPropertyName("title")]
            public string? Title { get; set; }

            [JsonPropertyName("message")]
            public string? Message { get; set; }

            [JsonPropertyName("deviceId")]
            public string? DeviceId { get; set; }

            [JsonPropertyName("taskId")]
            public string? TaskId { get; set; }

            [JsonPropertyName("scheduledUtc")]
            public DateTime? ScheduledUtc { get; set; }

            [JsonPropertyName("delay")]
            public TimeSpan? Delay { get; set; }

            [JsonPropertyName("isReminder")]
            public bool? IsReminder { get; set; }

            [JsonPropertyName("occuredAt")]
            public DateTime? OccuredAt { get; set; }

            [JsonPropertyName("eventId")]
            public Guid? EventId { get; set; }
        }
    }
}
