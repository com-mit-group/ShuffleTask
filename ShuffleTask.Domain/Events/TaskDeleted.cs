using System.Text.Json.Serialization;
using Yaref92.Events;

namespace ShuffleTask.Domain.Events;

public sealed class TaskDeleted : DomainEventBase
{
    [JsonConstructor]
    public TaskDeleted(string taskId, string deviceId, DateTime deletedAt, DateTime? occuredAt = null, Guid? eventId = null)
        : base(occuredAt, eventId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw new ArgumentException("Task id must be provided.", nameof(taskId));
        }

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ArgumentException("Device id must be provided.", nameof(deviceId));
        }

        TaskId = taskId;
        DeviceId = deviceId;
        DeletedAt = EnsureUtc(deletedAt);
    }

    public string TaskId { get; }

    public string DeviceId { get; }

    public DateTime DeletedAt { get; }

    private static DateTime EnsureUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }

        return DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);
    }
}
