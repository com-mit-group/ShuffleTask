using Microsoft.Extensions.Logging;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Events;
using ShuffleTask.Domain.Entities;
using System;
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
            TaskItem? existing = await _storage.GetTaskAsync(domainEvent.Task.Id).ConfigureAwait(false);
            TaskItem incoming = TaskSyncMerge.NormalizeIncoming(domainEvent.Task, existing);

            if (existing == null)
            {
                await _storage.AddTaskAsync(incoming).ConfigureAwait(false);
                return;
            }

            if (TaskSyncMerge.IsStaleEventTask(incoming, existing))
            {
                _logger?.LogInformation(
                    "Ignoring stale task update for {TaskId} with version {Version}",
                    incoming.Id,
                    incoming.EventVersion);
                return;
            }

            incoming.CreatedAt = existing.CreatedAt;
            await _storage.UpdateTaskAsync(incoming).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to apply inbound task upsert for {TaskId}", domainEvent.Task.Id);
        }
    }

}
