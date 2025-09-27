using System;
using Microsoft.Maui.Storage;
using ShuffleTask.Models;
using ShuffleTask.Services;
using ShuffleTask.Views;

namespace ShuffleTask;

public partial class App : Application
{
    private readonly IStorageService _storage;
    private readonly ShuffleCoordinatorService _coordinator;
    private readonly TimeProvider _clock;

    public App(MainPage mainPage, IStorageService storage, ShuffleCoordinatorService coordinator, TimeProvider clock)
    {
        InitializeComponent();
        MainPage = mainPage;
        _storage = storage;
        _coordinator = coordinator;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        RequestedThemeChanged += (_, __) => { };
    }

    protected override async void OnStart()
    {
        base.OnStart();
        await EnsureSeedDataAsync();
        await _coordinator.StartAsync();
    }

    protected override async void OnResume()
    {
        base.OnResume();
        await _coordinator.ResumeAsync();
    }

    protected override async void OnSleep()
    {
        await _coordinator.PauseAsync();
        base.OnSleep();
    }

    private async Task EnsureSeedDataAsync()
    {
        await _storage.InitializeAsync();
        var settings = await _storage.GetSettingsAsync();
        var existing = await _storage.GetTasksAsync();
        if (existing.Count > 0)
        {
            return;
        }

        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;

        var samples = new List<TaskItem>
        {
            new TaskItem { Title = "Dishes", Importance = 3, Repeat = RepeatType.Daily, AllowedPeriod = AllowedPeriod.Off },
            new TaskItem { Title = "Inbox Zero", Importance = 4, Repeat = RepeatType.Interval, IntervalDays = 2, AllowedPeriod = AllowedPeriod.Work },
            new TaskItem { Title = "Laundry", Importance = 2, Repeat = RepeatType.Weekly, Weekdays = Weekdays.Sat, AllowedPeriod = AllowedPeriod.Off },
            new TaskItem { Title = "Tax paperwork", Importance = 5, Repeat = RepeatType.None, Deadline = nowUtc.AddDays(3), AllowedPeriod = AllowedPeriod.Any }
        };

        foreach (var task in samples)
        {
            await _storage.AddTaskAsync(task);
        }

        settings.WorkStart = new TimeSpan(9, 0, 0);
        settings.WorkEnd = new TimeSpan(17, 0, 0);
        settings.EnableNotifications = true;
        settings.SoundOn = true;
        settings.Active = true;
        settings.AutoShuffleEnabled = true;
        settings.ReminderMinutes = 60;
        settings.MaxDailyShuffles = 6;
        settings.QuietHoursStart = new TimeSpan(22, 0, 0);
        settings.QuietHoursEnd = new TimeSpan(7, 0, 0);
        settings.StreakBias = 0.3;
        settings.StableRandomnessPerDay = true;
        await _storage.SetSettingsAsync(settings);

        Preferences.Default.Remove(PreferenceKeys.RemainingSeconds);
        Preferences.Default.Remove(PreferenceKeys.CurrentTaskId);
        Preferences.Default.Remove(PreferenceKeys.NextShuffleAt);
        Preferences.Default.Remove(PreferenceKeys.PendingShuffleTaskId);
        Preferences.Default.Remove(PreferenceKeys.ShuffleCount);
        Preferences.Default.Remove(PreferenceKeys.ShuffleCountDate);
    }
}
