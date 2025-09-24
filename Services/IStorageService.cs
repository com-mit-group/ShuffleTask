using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ShuffleTask.Models;

namespace ShuffleTask.Services;

public interface IStorageService
{
    Task InitializeAsync();
    Task<List<TaskItem>> GetTasksAsync();
    Task<TaskItem?> GetTaskAsync(string id);
    Task AddTaskAsync(TaskItem item);
    Task UpdateTaskAsync(TaskItem item);
    Task DeleteTaskAsync(string id);
    Task<TaskItem?> MarkTaskDoneAsync(string id);
    Task<TaskItem?> SnoozeTaskAsync(string id, TimeSpan duration);
    Task<TaskItem?> ResumeTaskAsync(string id);
    Task<AppSettings> GetSettingsAsync();
    Task SetSettingsAsync(AppSettings settings);
}
