using Yaref92.Events;

namespace ShuffleTask.Application.Events;

public class TimeUpNotificationEvent(string deviceId, string? userId) : DomainEventBase()
{
    public const string TimeUpTitle = "Time's up";
    public const string TimeUpMessage = "Shuffling a new task...";

    public string DeviceId { get; } = deviceId;

    public string? UserId { get; } = userId;
}