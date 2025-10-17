using System.Text.Json.Serialization;
using ShuffleTask.Domain.Entities;
using Yaref92.Events;

namespace ShuffleTask.Domain.Events;

public sealed class TaskUpserted : DomainEventBase
{
    [JsonConstructor]
    public TaskUpserted(TaskItem task, string deviceId, DateTime updatedAt, DateTime? occuredAt = null, Guid? eventId = null)
        : base(occuredAt, eventId)
    {
        Task = task ?? throw new ArgumentNullException(nameof(task));
        DeviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
        UpdatedAt = EnsureUtc(updatedAt);
    }

    public TaskItem Task { get; }

    public string DeviceId { get; }

    public DateTime UpdatedAt { get; }

    private static DateTime EnsureUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }

        return DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);
    }
}
