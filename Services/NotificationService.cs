using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using ShuffleTask.Models;

namespace ShuffleTask.Services;

/// <summary>
/// Provides cross-platform notifications using platform primitives with a XAML alert fallback.
/// </summary>
public partial class NotificationService
{
    private readonly INotificationPlatform _platform;

    public NotificationService()
    {
        INotificationPlatform platform = new DefaultNotificationPlatform();
        InitializePlatform(ref platform);
        _platform = platform;
    }

    /// <summary>
    /// Allows platform-specific partial implementations to provide an OS-backed notification engine.
    /// </summary>
    /// <param name="platform">Reference to the platform implementation that may be replaced.</param>
    partial void InitializePlatform(ref INotificationPlatform platform);

    public Task InitializeAsync() => _platform.InitializeAsync();

    public Task NotifyTaskAsync(TaskItem task, int minutes, AppSettings settings)
        => NotifyTaskAsync(task, minutes, settings, delay: TimeSpan.Zero);

    public async Task NotifyTaskAsync(TaskItem task, int minutes, AppSettings settings, TimeSpan delay)
    {
        if (!settings.EnableNotifications)
        {
            return;
        }

        string title = "Reminder";
        string message = $"Time for: {task.Title}\nYou have {minutes} minutes.";

        await NotifyAsync(title, message, delay, settings);
    }

    public async Task ShowToastAsync(string title, string message, AppSettings settings)
    {
        if (!settings.EnableNotifications)
        {
            return;
        }

        await _platform.ShowToastAsync(title, message, settings.SoundOn);
    }

    public Task NotifyPhaseAsync(string title, string message, TimeSpan delay, AppSettings settings)
    {
        if (!settings.EnableNotifications)
        {
            return Task.CompletedTask;
        }

        return NotifyAsync(title, message, delay, settings);
    }

    private Task NotifyAsync(string title, string message, TimeSpan delay, AppSettings settings)
        => _platform.NotifyAsync(title, message, delay, settings.SoundOn);

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

    private interface INotificationPlatform
    {
        Task InitializeAsync();

        Task NotifyAsync(string title, string message, TimeSpan delay, bool playSound);

        Task ShowToastAsync(string title, string message, bool playSound);
    }

    private sealed class DefaultNotificationPlatform : INotificationPlatform
    {
        public Task InitializeAsync() => Task.CompletedTask;

        public async Task NotifyAsync(string title, string message, TimeSpan delay, bool playSound)
        {
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

            await ShowAlertAsync(title, message);
        }

        public Task ShowToastAsync(string title, string message, bool playSound)
            => ShowAlertAsync(title, message);
    }
}
