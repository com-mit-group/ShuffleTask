using System.Text.Json.Serialization;
using Yaref92.Events;

namespace ShuffleTask.Domain.Events;

public sealed class NotificationBroadcasted : DomainEventBase
{
    [JsonConstructor]
    public NotificationBroadcasted(
        string notificationId,
        string title,
        string message,
        string deviceId,
        string? taskId,
        DateTime scheduledUtc,
        TimeSpan? delay,
        bool isReminder,
        DateTime? occuredAt = null,
        Guid? eventId = null)
        : base(occuredAt ?? default, eventId ?? Guid.Empty)
    {
        if (string.IsNullOrWhiteSpace(notificationId))
        {
            throw new ArgumentException("Notification id must be provided.", nameof(notificationId));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Notification title must be provided.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Notification message must be provided.", nameof(message));
        }

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ArgumentException("Device id must be provided.", nameof(deviceId));
        }

        NotificationId = notificationId;
        Title = title;
        Message = message;
        DeviceId = deviceId;
        TaskId = taskId;
        ScheduledUtc = EnsureUtc(scheduledUtc);
        Delay = delay;
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

    private static DateTime EnsureUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }

        return DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);
    }
}
