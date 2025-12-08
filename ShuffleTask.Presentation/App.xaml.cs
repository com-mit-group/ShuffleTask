using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Presentation;
using ShuffleTask.Presentation.Services;
using ShuffleTask.Views;

namespace ShuffleTask;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly IStorageService _storage;
    private readonly ShuffleCoordinatorService _coordinator;
    private readonly TimeProvider _clock;
    private readonly AppSettings _settings;

    public App(MainPage mainPage, IStorageService storage, ShuffleCoordinatorService coordinator, TimeProvider clock, AppSettings settings)
    {
        InitializeComponent();
        MainPage = mainPage;
        _storage = storage;
        _coordinator = coordinator;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
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

    protected override void OnSleep()
    {
        base.OnSleep();
        _coordinator.SuspendInProcessTimer();
    }

    private async Task EnsureSeedDataAsync()
    {
        await _storage.InitializeAsync();
        var existing = await _storage.GetTasksAsync();
        if (existing.Count > 0)
        {
            return;
        }

        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;

        var samples = new List<TaskItem>
        {
            new TaskItem { Title = "Dishes", Importance = 3, Repeat = RepeatType.Daily, AllowedPeriod = AllowedPeriod.OffWork },
            new TaskItem { Title = "Inbox Zero", Importance = 4, Repeat = RepeatType.Interval, IntervalDays = 2, AllowedPeriod = AllowedPeriod.Work },
            new TaskItem { Title = "Laundry", Importance = 2, Repeat = RepeatType.Weekly, Weekdays = Weekdays.Sat, AllowedPeriod = AllowedPeriod.OffWork },
            new TaskItem { Title = "Tax paperwork", Importance = 5, Repeat = RepeatType.None, Deadline = nowUtc.AddDays(3), AllowedPeriod = AllowedPeriod.Any }
        };

        foreach (var task in samples)
        {
            await _storage.AddTaskAsync(task);
        }

        _settings.WorkStart = new TimeSpan(9, 0, 0);
        _settings.WorkEnd = new TimeSpan(17, 0, 0);
        _settings.EnableNotifications = true;
        _settings.SoundOn = true;
        _settings.Active = true;
        _settings.AutoShuffleEnabled = true;
        _settings.ReminderMinutes = 60;
        _settings.MaxDailyShuffles = 6;
        _settings.QuietHoursStart = new TimeSpan(22, 0, 0);
        _settings.QuietHoursEnd = new TimeSpan(7, 0, 0);
        _settings.StreakBias = 0.3;
        _settings.StableRandomnessPerDay = true;
        await _storage.SetSettingsAsync(_settings);

        Preferences.Default.Remove(PreferenceKeys.TimerDurationSeconds);
        Preferences.Default.Remove(PreferenceKeys.TimerExpiresAt);
        Preferences.Default.Remove(PreferenceKeys.CurrentTaskId);
        Preferences.Default.Remove(PreferenceKeys.NextShuffleAt);
        Preferences.Default.Remove(PreferenceKeys.PendingShuffleTaskId);
        Preferences.Default.Remove(PreferenceKeys.ShuffleCount);
        Preferences.Default.Remove(PreferenceKeys.ShuffleCountDate);
    }
}
