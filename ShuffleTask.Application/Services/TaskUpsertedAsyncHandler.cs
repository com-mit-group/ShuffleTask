using Microsoft.Extensions.Logging;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Events;
using Yaref92.Events.Abstractions;

namespace ShuffleTask.Application.Services;
internal class TaskUpsertedAsyncHandler : IAsyncEventHandler<TaskUpsertedEvent>
{
    private readonly ILogger<NetworkSyncService>? _logger;
    private readonly IStorageService _storage;

    public TaskUpsertedAsyncHandler(ILogger<NetworkSyncService>? logger, IStorageService storage)
    {
        _logger = logger;
        _storage = storage;
    }

    public async Task OnNextAsync(TaskUpsertedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        if (domainEvent == null || domainEvent.Task == null)
        {
            throw new ArgumentNullException(nameof(domainEvent), "TaskUpsertedEvent or its task are null");
        }

        try
        {
            Domain.Entities.TaskItem? existing = await _storage.GetTaskAsync(domainEvent.Task.Id).ConfigureAwait(false);
            if (existing == null)
            {
                await _storage.AddTaskAsync(domainEvent.Task).ConfigureAwait(false);
            }
            else
            {
                await _storage.UpdateTaskAsync(domainEvent.Task).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to apply inbound task upsert for {TaskId}", domainEvent.Task.Id);
        }
    }
}