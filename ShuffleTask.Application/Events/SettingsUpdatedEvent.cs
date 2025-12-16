using ShuffleTask.Application.Models;
using Yaref92.Events;

namespace ShuffleTask.Application.Events;

public class SettingsUpdatedEvent(AppSettings settings, string deviceId, string? userId) : DomainEventBase()
{
    public AppSettings Settings { get; set; } = settings;

    public string DeviceId { get; set; } = deviceId;

    public string? UserId { get; set; } = userId;
}
