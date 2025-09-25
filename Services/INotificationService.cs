using ShuffleTask.Models;

namespace ShuffleTask.Services;

public interface INotificationService
{
    Task InitializeAsync();

    Task NotifyTaskAsync(TaskItem task, int minutes, AppSettings settings);

    Task NotifyTaskAsync(TaskItem task, int minutes, AppSettings settings, TimeSpan delay);

    Task ShowToastAsync(string title, string message, AppSettings settings);
}
