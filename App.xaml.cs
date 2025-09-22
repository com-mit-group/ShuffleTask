using System;
using Microsoft.Maui.Storage;
using ShuffleTask.Models;
using ShuffleTask.Services;
using ShuffleTask.Views;

namespace ShuffleTask;

public partial class App : Application
{
    private readonly StorageService _storage;

    private const string PrefTaskId = "pref.currentTaskId";
    private const string PrefRemainingSecs = "pref.remainingSecs";

    public App(MainPage mainPage, StorageService storage)
    {
        InitializeComponent();
        MainPage = mainPage;
        _storage = storage;
        RequestedThemeChanged += (_, __) => { };
    }

    protected override async void OnStart()
    {
        base.OnStart();
        await EnsureSeedDataAsync();
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

        var samples = new List<TaskItem>
        {
            new TaskItem { Title = "Dishes", Importance = 3, Repeat = RepeatType.Daily, AllowedPeriod = AllowedPeriod.Off },
            new TaskItem { Title = "Inbox Zero", Importance = 4, Repeat = RepeatType.Interval, IntervalDays = 2, AllowedPeriod = AllowedPeriod.Work },
            new TaskItem { Title = "Laundry", Importance = 2, Repeat = RepeatType.Weekly, Weekdays = Weekdays.Sat, AllowedPeriod = AllowedPeriod.Off },
            new TaskItem { Title = "Tax paperwork", Importance = 5, Repeat = RepeatType.None, Deadline = DateTime.Now.AddDays(3), AllowedPeriod = AllowedPeriod.Any }
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
        settings.ReminderMinutes = 60;
        settings.StreakBias = 0.3;
        settings.StableRandomnessPerDay = true;
        await _storage.SetSettingsAsync(settings);

        Preferences.Default.Remove(PrefRemainingSecs);
        Preferences.Default.Remove(PrefTaskId);
    }
}
