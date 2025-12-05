using Microsoft.Extensions.Logging;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Events;
using Yaref92.Events.Abstractions;

namespace ShuffleTask.Application.Services;
internal class TaskDeletedAsyncHandler(ILogger<NetworkSyncService>? logger, IStorageService storage) : IAsyncEventHandler<TaskDeletedEvent>
{
    private readonly ILogger<NetworkSyncService>? _logger = logger;
    private readonly IStorageService _storage = storage;

    public async Task OnNextAsync(TaskDeletedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        if (domainEvent == null || string.IsNullOrWhiteSpace(domainEvent.TaskId))
        {
            throw new ArgumentNullException(nameof(domainEvent), "TaskDeletedEvent or its taskId are null");
        }

        try
        {
            await _storage.DeleteTaskAsync(domainEvent.TaskId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to apply inbound task delete for {TaskId}", domainEvent.TaskId);
        }
    }
}