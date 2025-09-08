using Plugin.LocalNotification;
#if ANDROID
using Plugin.LocalNotification.AndroidOption;
#endif
using ShuffleTask.Models;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace ShuffleTask.Services;

public class NotificationService
{
    private bool _initialized;

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        try
        {
#if WINDOWS
            // On Windows, avoid initializing Plugin.LocalNotification to prevent WinRT/Toolkit exceptions.
            // We'll fallback to simple in-app alerts for notifications.
#else
            await LocalNotificationCenter.Current.RequestNotificationPermission();
#endif
        }
        catch { }

#if ANDROID
        IList<NotificationChannelRequest> channels = [new NotificationChannelRequest
        {
            Id = "shuffletask.default",
            Name = "ShuffleTask",
            Importance = AndroidImportance.Default
        }];
        LocalNotificationCenter.CreateNotificationChannels(channels);
#endif

        _initialized = true;
    }

    private int? _lastNotificationId;

    public Task NotifyTaskAsync(TaskItem t, int minutes, AppSettings settings)
        => NotifyTaskAsync(t, minutes, settings, delay: TimeSpan.Zero);

    public async Task NotifyTaskAsync(TaskItem t, int minutes, AppSettings settings, TimeSpan delay)
    {
        await InitializeAsync();

#if WINDOWS
        // Fallback to an in-app alert on Windows to avoid WinRT exceptions from toast APIs.
        if (Application.Current?.MainPage != null)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "Reminder",
                        $"Time for: {t.Title}\nYou have {minutes} minutes.",
                        "OK");
                }
                catch { }
            });
        }
        return;
#else
        // Cancel previous to avoid duplicates
        if (_lastNotificationId.HasValue)
        {
            LocalNotificationCenter.Current.Cancel(_lastNotificationId.Value);
            _lastNotificationId = null;
        }

        int id = Math.Abs(t.Id.GetHashCode());
        var request = new NotificationRequest
        {
            NotificationId = id,
            Title = $"Time for: {t.Title}",
            Description = $"You have {minutes} minutes.",
            ReturningData = t.Id,
            CategoryType = NotificationCategoryType.Reminder,
            Schedule = new NotificationRequestSchedule
            {
                NotifyTime = DateTime.Now.Add(delay)
            }
        };

        if (!settings.SoundOn)
        {
            request.Sound = string.Empty; // mute
        }

        await LocalNotificationCenter.Current.Show(request);
        _lastNotificationId = id;
#endif
    }

    public async Task ShowToastAsync(string title, string message, AppSettings settings)
    {
        await InitializeAsync();

#if WINDOWS
        // Fallback to in-app alert on Windows
        if (Application.Current?.MainPage != null)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    await Application.Current.MainPage.DisplayAlert(title, message, "OK");
                }
                catch { }
            });
        }
        return;
#else
        // Cancel previous toast to avoid stacking
        if (_lastNotificationId.HasValue)
        {
            LocalNotificationCenter.Current.Cancel(_lastNotificationId.Value);
            _lastNotificationId = null;
        }

        int id = Math.Abs((title + message).GetHashCode());
        var request = new NotificationRequest
        {
            NotificationId = id,
            Title = title,
            Description = message,
            CategoryType = NotificationCategoryType.Reminder,
            Schedule = new NotificationRequestSchedule { NotifyTime = DateTime.Now }
        };

        if (!settings.SoundOn)
        {
            request.Sound = string.Empty; // mute
        }

        await LocalNotificationCenter.Current.Show(request);
        _lastNotificationId = id;
#endif
    }
}
