using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Abstractions;

public interface IStorageService
{
    Task InitializeAsync();
    Task<List<TaskItem>> GetTasksAsync(string? userId = "", string deviceId = "");
    Task<TaskItem?> GetTaskAsync(string id);
    Task AddTaskAsync(TaskItem item);
    Task UpdateTaskAsync(TaskItem item);
    Task DeleteTaskAsync(string id);
    Task<TaskItem?> MarkTaskDoneAsync(string id);
    Task<TaskItem?> SnoozeTaskAsync(string id, TimeSpan duration);
    Task<TaskItem?> ResumeTaskAsync(string id);
    Task<AppSettings> GetSettingsAsync();
    Task SetSettingsAsync(AppSettings settings);
    Task<int> MigrateDeviceTasksToUserAsync(string deviceId, string userId);
}
