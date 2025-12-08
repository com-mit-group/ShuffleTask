using Microsoft.Extensions.Logging;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Events;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Services;
using ShuffleTask.ViewModels;
using Yaref92.Events.Abstractions;

namespace ShuffleTask.Presentation.EventsHandlers;

internal class TaskStartedAsyncHandler(ILogger<NetworkSyncService>? logger, IStorageService storage, INotificationService notifications, AppSettings settings) : IAsyncEventHandler<TaskStarted>
{
    private readonly ILogger<NetworkSyncService>? _logger = logger;
    private readonly IStorageService _storage = storage;
    private readonly INotificationService _notifications = notifications;
    private readonly AppSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private WeakReference<DashboardViewModel>? _dashboardRef;

    public async Task OnNextAsync(TaskStarted domainEvent, CancellationToken cancellationToken = default)
    {
        string receivedFromIdMessage = GetReceivedFromIdMessage(domainEvent);

        _logger?.LogInformation("Started task[{TaskId}]. {Minutes} in timer. Received {From}", domainEvent.TaskId, domainEvent.Minutes, receivedFromIdMessage);
        Domain.Entities.TaskItem? task = await _storage.GetTaskAsync(domainEvent.TaskId ?? string.Empty);
        if (task is not null)
        {
            if (_dashboardRef != null && _dashboardRef.TryGetTarget(out DashboardViewModel? dashboard))
            {
                Task applyTask = MainThread.InvokeOnMainThreadAsync(() => dashboard.ApplyAutoOrCrossDeviceShuffleAsync(task, _settings));
                await applyTask.ConfigureAwait(false);
            }
            await _notifications.NotifyTaskAsync(task, domainEvent.Minutes, _settings).ConfigureAwait(false);
        }
    }

    private static string GetReceivedFromIdMessage(TaskStarted domainEvent)
    {
        return string.IsNullOrEmpty(domainEvent.UserId)
            ? FromDevice(domainEvent)
            : $"from another device used by user {domainEvent.UserId}";
    }

    private static string FromDevice(TaskStarted domainEvent)
    {
        return domainEvent.DeviceId == Environment.MachineName ? "from this device" : $"from {domainEvent.DeviceId}";
    }

    internal void RegisterDashboard(DashboardViewModel dashboardViewModel)
    {
        _dashboardRef = new WeakReference<DashboardViewModel>(dashboardViewModel);
    }
}
