using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;
using System.Collections.Concurrent;

namespace ShuffleTask.Application.Tests.TestDoubles;

internal sealed class InMemoryStorageService : IStorageService
{
    private readonly ConcurrentDictionary<string, TaskItem> _tasks = new(StringComparer.Ordinal);
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

        return Task.FromResult(query.Select(Clone).ToList());
    }

    public Task<TaskItem?> GetTaskAsync(string id)
    {
        EnsureInitialized();
        return Task.FromResult(_tasks.TryGetValue(id, out var value) ? Clone(value) : null);
    }

    public Task AddTaskAsync(TaskItem item)
    {
        EnsureInitialized();
        NormalizeMetadata(item, null, bumpVersion: true);
        _tasks[item.Id] = Clone(item);
        return Task.CompletedTask;
    }

    public Task UpdateTaskAsync(TaskItem item)
    {
        EnsureInitialized();
        NormalizeMetadata(item, _tasks.TryGetValue(item.Id, out var existing) ? existing : null, bumpVersion: true);
        _tasks[item.Id] = Clone(item);
        return Task.CompletedTask;
    }

    public Task DeleteTaskAsync(string id)
    {
        _tasks.TryRemove(id, out _);
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

    private static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
