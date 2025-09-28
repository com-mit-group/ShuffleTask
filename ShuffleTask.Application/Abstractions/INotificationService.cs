using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Abstractions;

public interface INotificationService
{
    Task InitializeAsync();
    Task NotifyPhaseAsync(string title, string message, TimeSpan delay, AppSettings settings);
    Task NotifyTaskAsync(TaskItem task, int minutes, AppSettings settings);

    Task NotifyTaskAsync(TaskItem task, int minutes, AppSettings settings, TimeSpan delay);

    Task ShowToastAsync(string title, string message, AppSettings settings);
}
