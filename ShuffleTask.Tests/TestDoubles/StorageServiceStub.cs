using ShuffleTask.Models;

namespace ShuffleTask.Services;

public class StorageService
{
    private readonly Dictionary<string, TaskItem> _tasks = new();
    private bool _initialized;

    public int InitializeCallCount { get; private set; }
    public int GetTasksCallCount { get; private set; }
    public int UpdateTaskCallCount { get; private set; }
    public int DeleteTaskCallCount { get; private set; }

    public Task InitializeAsync()
    {
        InitializeCallCount++;
        _initialized = true;
        return Task.CompletedTask;
    }

    public Task<List<TaskItem>> GetTasksAsync()
    {
        EnsureInitialized();
        GetTasksCallCount++;
        var snapshot = _tasks.Values
            .OrderByDescending(t => t.CreatedAt)
            .Select(Clone)
            .ToList();
        return Task.FromResult(snapshot);
    }

    public Task<TaskItem?> GetTaskAsync(string id)
    {
        EnsureInitialized();
        return Task.FromResult(_tasks.TryGetValue(id, out var item) ? Clone(item) : null);
    }

    public Task AddTaskAsync(TaskItem item)
    {
        EnsureInitialized();
        _tasks[item.Id] = Clone(item);
        return Task.CompletedTask;
    }

    public Task UpdateTaskAsync(TaskItem item)
    {
        EnsureInitialized();
        UpdateTaskCallCount++;
        _tasks[item.Id] = Clone(item);
        return Task.CompletedTask;
    }

    public Task DeleteTaskAsync(string id)
    {
        EnsureInitialized();
        DeleteTaskCallCount++;
        _tasks.Remove(id);
        return Task.CompletedTask;
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("StorageService not initialized. Call InitializeAsync() first.");
        }
    }

    private static TaskItem Clone(TaskItem task)
    {
        return new TaskItem
        {
            Id = task.Id,
            Title = task.Title,
            Description = task.Description,
            Importance = task.Importance,
            Deadline = task.Deadline,
            Repeat = task.Repeat,
            Weekdays = task.Weekdays,
            IntervalDays = task.IntervalDays,
            LastDoneAt = task.LastDoneAt,
            AllowedPeriod = task.AllowedPeriod,
            Paused = task.Paused,
            CreatedAt = task.CreatedAt
        };
    }
}
