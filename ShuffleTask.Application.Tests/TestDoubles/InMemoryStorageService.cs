using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;
using System.Collections.Concurrent;

namespace ShuffleTask.Application.Tests.TestDoubles;

internal sealed class InMemoryStorageService : IStorageService
{
    private readonly ConcurrentDictionary<string, TaskItem> _tasks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PeriodDefinition> _periodDefinitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _clock;
    private bool _initialized;
    private readonly AppSettings _settings;

    public InMemoryStorageService(TimeProvider? clock = null, AppSettings? settings = null)
    {
        _clock = clock ?? TimeProvider.System;
        _settings = settings ?? new AppSettings();
    }

    public Task InitializeAsync()
    {
        _initialized = true;
        return Task.CompletedTask;
    }

    public Task<List<TaskItem>> GetTasksAsync(string? userId = "", string deviceId = "")
    {
        EnsureInitialized();
        var query = _tasks.Values.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(t => t.UserId == userId);
        }
        else if (!string.IsNullOrWhiteSpace(deviceId))
        {
            query = query.Where(t => string.IsNullOrWhiteSpace(t.UserId) && string.Equals(t.DeviceId, deviceId, StringComparison.Ordinal));
        }

        var tasks = query.Select(Clone).ToList();
        ApplyPeriodDefinitions(tasks);
        return Task.FromResult(tasks);
    }

    public Task<TaskItem?> GetTaskAsync(string id)
    {
        EnsureInitialized();
        if (!_tasks.TryGetValue(id, out var value))
        {
            return Task.FromResult<TaskItem?>(null);
        }

        var clone = Clone(value);
        ApplyPeriodDefinition(clone);
        return Task.FromResult<TaskItem?>(clone);
    }

    public Task AddTaskAsync(TaskItem item)
    {
        EnsureInitialized();
        NormalizeMetadata(item, null, bumpVersion: true);
        _tasks[item.Id] = NormalizePeriodDefinition(item);
        return Task.CompletedTask;
    }

    public Task UpdateTaskAsync(TaskItem item)
    {
        EnsureInitialized();
        NormalizeMetadata(item, _tasks.TryGetValue(item.Id, out var existing) ? existing : null, bumpVersion: true);
        _tasks[item.Id] = NormalizePeriodDefinition(item);
        return Task.CompletedTask;
    }

    public Task DeleteTaskAsync(string id)
    {
        _tasks.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<List<PeriodDefinition>> GetPeriodDefinitionsAsync()
    {
        EnsureInitialized();
        var definitions = _periodDefinitions.Values
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .Select(Clone)
            .ToList();
        return Task.FromResult(definitions);
    }

    public Task<PeriodDefinition?> GetPeriodDefinitionAsync(string id)
    {
        EnsureInitialized();
        return Task.FromResult(_periodDefinitions.TryGetValue(id, out var definition) ? Clone(definition) : null);
    }

    public Task AddPeriodDefinitionAsync(PeriodDefinition definition)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            definition.Id = Guid.NewGuid().ToString("n");
        }

        _periodDefinitions[definition.Id] = Clone(definition);
        return Task.CompletedTask;
    }

    public Task UpdatePeriodDefinitionAsync(PeriodDefinition definition)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            definition.Id = Guid.NewGuid().ToString("n");
        }

        _periodDefinitions[definition.Id] = Clone(definition);
        return Task.CompletedTask;
    }

    public Task DeletePeriodDefinitionAsync(string id)
    {
        EnsureInitialized();
        _periodDefinitions.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<TaskItem?> MarkTaskDoneAsync(string id) => Task.FromResult<TaskItem?>(null);
    public Task<TaskItem?> SnoozeTaskAsync(string id, TimeSpan duration) => Task.FromResult<TaskItem?>(null);
    public Task<TaskItem?> ResumeTaskAsync(string id) => Task.FromResult<TaskItem?>(null);

    public Task<AppSettings> GetSettingsAsync()
    {
        EnsureInitialized();
        return Task.FromResult(_settings);
    }

    public Task SetSettingsAsync(AppSettings settings)
    {
        EnsureInitialized();
        StampSettings(settings);
        _settings.CopyFrom(settings);
        return Task.CompletedTask;
    }

    public Task<int> MigrateDeviceTasksToUserAsync(string deviceId, string userId) => Task.FromResult(0);

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Storage not initialized");
        }
    }

    private void StampSettings(AppSettings settings)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        settings.UpdatedAt = settings.UpdatedAt == default ? now : settings.UpdatedAt;
        settings.EventVersion = settings.EventVersion <= 0 ? _settings.EventVersion + 1 : settings.EventVersion;
    }

    private void NormalizeMetadata(TaskItem target, TaskItem? existing, bool bumpVersion)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        target.Id = string.IsNullOrWhiteSpace(target.Id) ? Guid.NewGuid().ToString("n") : target.Id;
        target.CreatedAt = target.CreatedAt == default ? now : EnsureUtc(target.CreatedAt);
        target.UpdatedAt = target.UpdatedAt == default ? now : EnsureUtc(target.UpdatedAt);

        var existingVersion = existing?.EventVersion ?? 0;
        target.EventVersion = bumpVersion ? Math.Max(existingVersion + 1, target.EventVersion) : Math.Max(existingVersion, target.EventVersion);
        target.UserId = string.IsNullOrWhiteSpace(target.UserId) ? existing?.UserId : target.UserId;
        target.DeviceId = string.IsNullOrWhiteSpace(target.UserId)
            ? (string.IsNullOrWhiteSpace(target.DeviceId) ? existing?.DeviceId ?? Environment.MachineName : target.DeviceId)
            : null;
    }

    private static TaskItem Clone(TaskItem task) => TaskItem.Clone(task);

    private static PeriodDefinition Clone(PeriodDefinition definition)
    {
        return new PeriodDefinition
        {
            Id = definition.Id,
            Name = definition.Name,
            Weekdays = definition.Weekdays,
            StartTime = definition.StartTime,
            EndTime = definition.EndTime,
            IsAllDay = definition.IsAllDay,
            Mode = definition.Mode
        };
    }

    private TaskItem NormalizePeriodDefinition(TaskItem task)
    {
        var clone = Clone(task);
        if (string.IsNullOrWhiteSpace(clone.PeriodDefinitionId))
        {
            return clone;
        }

        clone.AdHocStartTime = null;
        clone.AdHocEndTime = null;
        clone.AdHocWeekdays = null;
        clone.AdHocIsAllDay = false;
        clone.AdHocMode = PeriodDefinitionMode.None;
        return clone;
    }

    private void ApplyPeriodDefinitions(IReadOnlyList<TaskItem> tasks)
    {
        foreach (var task in tasks)
        {
            ApplyPeriodDefinition(task);
        }
    }

    private void ApplyPeriodDefinition(TaskItem task)
    {
        if (string.IsNullOrWhiteSpace(task.PeriodDefinitionId))
        {
            return;
        }

        if (PeriodDefinitionCatalog.TryGet(task.PeriodDefinitionId, out _))
        {
            return;
        }

        if (!_periodDefinitions.TryGetValue(task.PeriodDefinitionId, out var definition))
        {
            return;
        }

        task.AdHocStartTime = definition.StartTime;
        task.AdHocEndTime = definition.EndTime;
        task.AdHocWeekdays = definition.Weekdays;
        task.AdHocIsAllDay = definition.IsAllDay;
        task.AdHocMode = definition.Mode;
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
