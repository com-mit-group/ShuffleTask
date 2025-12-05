using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Presentation.Tests.TestSupport;

internal sealed class TestNotificationService : INotificationService
{
    public List<(string Title, string Message)> Shown { get; } = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task NotifyPhaseAsync(string title, string message, TimeSpan delay, AppSettings settings)
        => Task.CompletedTask;

    public Task NotifyTaskAsync(TaskItem task, int minutes, AppSettings settings)
        => Task.CompletedTask;

    public Task NotifyTaskAsync(TaskItem task, int minutes, AppSettings settings, TimeSpan delay)
        => Task.CompletedTask;

    public Task ShowToastAsync(string title, string message, AppSettings settings)
    {
        Shown.Add((title, message));
        return Task.CompletedTask;
    }
}