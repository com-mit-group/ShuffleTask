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
            TaskItem incoming = NormalizeIncoming(domainEvent.Task, existing);

            if (existing == null)
            {
                await _storage.AddTaskAsync(incoming).ConfigureAwait(false);
                return;
            }

            if (IsStale(incoming, existing))
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

    private static bool IsStale(TaskItem incoming, TaskItem existing)
    {
        if (incoming.EventVersion > 0 && incoming.EventVersion <= existing.EventVersion)
        {
            return true;
        }

        return incoming.EventVersion <= 0 && incoming.UpdatedAt != default && incoming.UpdatedAt <= existing.UpdatedAt;
    }

    private static TaskItem NormalizeIncoming(TaskItem task, TaskItem? existing)
    {
        TaskItem normalized = task.Clone();

        if (string.IsNullOrWhiteSpace(normalized.Id) && existing != null)
        {
            normalized.Id = existing.Id;
        }
        else if (string.IsNullOrWhiteSpace(normalized.Id))
        {
            normalized.Id = Guid.NewGuid().ToString("n");
        }

        if (normalized.CreatedAt == default)
        {
            normalized.CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow;
        }

        if (normalized.UpdatedAt == default)
        {
            normalized.UpdatedAt = existing?.UpdatedAt ?? DateTime.UtcNow;
        }

        if (normalized.EventVersion <= 0)
        {
            normalized.EventVersion = (existing?.EventVersion ?? 0) + 1;
        }

        if (!string.IsNullOrWhiteSpace(normalized.UserId))
        {
            normalized.DeviceId = null;
        }
        else
        {
            normalized.UserId = existing?.UserId;
            normalized.DeviceId = string.IsNullOrWhiteSpace(normalized.DeviceId)
                ? existing?.DeviceId ?? Environment.MachineName
                : normalized.DeviceId.Trim();
        }
        return normalized;
    }
}
