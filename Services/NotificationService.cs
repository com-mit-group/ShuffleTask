using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using ShuffleTask.Models;

namespace ShuffleTask.Services;

/// <summary>
/// Provides a lightweight in-app notification fallback that does not rely on external plugins.
/// </summary>
public class NotificationService
{
    public Task InitializeAsync() => Task.CompletedTask;

    public Task NotifyTaskAsync(TaskItem task, int minutes, AppSettings settings)
        => NotifyTaskAsync(task, minutes, settings, delay: TimeSpan.Zero);

    public async Task NotifyTaskAsync(TaskItem task, int minutes, AppSettings settings, TimeSpan delay)
    {
        if (!settings.EnableNotifications)
        {
            return;
        }

        if (delay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(delay);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }

        await ShowAlertAsync("Reminder", $"Time for: {task.Title}\nYou have {minutes} minutes.");
    }

    public async Task ShowToastAsync(string title, string message, AppSettings settings)
    {
        if (!settings.EnableNotifications)
        {
            return;
        }

        await ShowAlertAsync(title, message);
    }

    private static async Task ShowAlertAsync(string title, string message)
    {
        var page = Application.Current?.MainPage;
        if (page == null)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await page.DisplayAlert(title, message, "OK");
            }
            catch
            {
                // UI might be mid-transition; ignore notification failures.
            }
        });
    }
}
