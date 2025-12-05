using Microsoft.Extensions.Logging;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Events;
using ShuffleTask.Application.Services;
using ShuffleTask.Presentation.Utilities;
using Yaref92.Events.Abstractions;

namespace ShuffleTask.Presentation.EventsHandlers;
internal class TimeUpNotificationAsyncHandler(ILogger<NetworkSyncService>? logger, IStorageService storage, INotificationService notifications) : IAsyncEventHandler<TimeUpNotificationEvent>
{
    public const string TimeUpTitle = "Time's up";
    public const string TimeUpMessage = "Shuffling a new task...";

    public ILogger<NetworkSyncService>? Logger { get; } = logger;
    public IStorageService Storage { get; } = storage;
    public INotificationService Notifications { get; } = notifications;

    public async Task OnNextAsync(TimeUpNotificationEvent domainEvent, CancellationToken cancellationToken = default)
    {
        Models.AppSettings settings = await Storage.GetSettingsAsync().ConfigureAwait(false);
        Logger?.LogInformation("Time up for last task");
        await Notifications.ShowToastAsync(TimeUpNotificationEvent.TimeUpTitle, TimeUpNotificationEvent.TimeUpMessage, settings).ConfigureAwait(false);

        PersistedTimerState.Clear();
    }
}