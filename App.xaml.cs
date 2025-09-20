using ShuffleTask.Models;
using ShuffleTask.Services;
using ShuffleTask.ViewModels;
using ShuffleTask.Views;

namespace ShuffleTask;

public partial class App : Application
{
    private readonly DashboardPage _nowPage;
    private readonly DashboardViewModel _nowVm;
    private readonly StorageService _storage;
    private readonly SchedulerService _scheduler;

    private IDispatcherTimer? _delayTimer;

    private const string PrefTaskId = "pref.currentTaskId";
    private const string PrefRemainingSecs = "pref.remainingSecs";

    public App(MainPage mainPage, DashboardPage nowPage, DashboardViewModel nowVm, StorageService storage, SchedulerService scheduler)
    {
        InitializeComponent();
        MainPage = mainPage;
        _nowPage = nowPage;
        _nowVm = nowVm;
        _storage = storage;
        _scheduler = scheduler;

        _nowVm.DoneOccurred += async (_, __) => await AfterDoneOrSkipAsync();
        _nowVm.SkipOccurred += async (_, __) => await AfterDoneOrSkipAsync();

        RequestedThemeChanged += (_, __) => { };
    }

    protected override async void OnStart()
    {
        base.OnStart();
        await _storage.InitializeAsync();
        var settings = await _storage.GetSettingsAsync();

        // Seed sample data on first run if there are no tasks
        var existing = await _storage.GetTasksAsync();
        if (existing.Count == 0)
        {
            var samples = new List<TaskItem>
            {
                new TaskItem { Title = "Dishes", Importance = 3, Repeat = RepeatType.Daily, AllowedPeriod = AllowedPeriod.Off },
                new TaskItem { Title = "Inbox Zero", Importance = 4, Repeat = RepeatType.Interval, IntervalDays = 2, AllowedPeriod = AllowedPeriod.Work },
                new TaskItem { Title = "Laundry", Importance = 2, Repeat = RepeatType.Weekly, Weekdays = Weekdays.Sat, AllowedPeriod = AllowedPeriod.Off },
                new TaskItem { Title = "Tax paperwork", Importance = 5, Repeat = RepeatType.None, Deadline = DateTime.Now.AddDays(3), AllowedPeriod = AllowedPeriod.Any }
            };
            foreach (var t in samples) await _storage.AddTaskAsync(t);

            // Default settings
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

        if (!settings.Active)
            return;

        var persistedSecs = Preferences.Default.Get(PrefRemainingSecs, -1);
        var persistedId = Preferences.Default.Get(PrefTaskId, string.Empty);
        if (persistedSecs <= 0 || string.IsNullOrEmpty(persistedId))
        {
            EnsureDashboardTabActive();
        }
    }

    private async Task AfterDoneOrSkipAsync()
    {
        var settings = await _storage.GetSettingsAsync();
        TimeSpan gap = _scheduler.NextGap(settings, DateTime.Now);

        // cancel any previous delay timer
        _delayTimer?.Stop();
        _delayTimer = Application.Current!.Dispatcher.CreateTimer();
        _delayTimer.Interval = gap;
        _delayTimer.IsRepeating = false;
        _delayTimer.Tick += async (s, e) =>
        {
            _delayTimer?.Stop();
            await TryPickNextRespectingWindowAsync(settings);
        };
        _delayTimer.Start();
    }

    private async Task TryPickNextRespectingWindowAsync(AppSettings settings)
    {
        var tasks = await _storage.GetTasksAsync();
        var now = DateTime.Now;
        var picked = _scheduler.PickNextTask(tasks, settings, now);
        if (picked == null)
        {
            // Friendly notice and schedule retry at next boundary
            var until = TimeWindowService.UntilNextBoundary(now, settings);
            await new Services.NotificationService().ShowToastAsync("On a break", $"Next window in {Math.Max(1, (int)until.TotalMinutes)} min", settings);

            _delayTimer?.Stop();
            _delayTimer = Application.Current!.Dispatcher.CreateTimer();
            _delayTimer.Interval = until;
            _delayTimer.IsRepeating = false;
            _delayTimer.Tick += async (s, e) =>
            {
                _delayTimer?.Stop();
                await TryPickNextRespectingWindowAsync(settings);
            };
            _delayTimer.Start();
            return;
        }

        EnsureDashboardTabActive();

        await _nowVm.InitializeAsync();
        _nowVm.CurrentTask = picked;
        await _nowPage.BeginCountdownAsync(settings.ReminderMinutes);
    }

    private void EnsureDashboardTabActive()
    {
        if (MainPage is not TabbedPage tabs)
            return;

        Page? target = null;

        foreach (var child in tabs.Children)
        {
            if (ReferenceEquals(child, _nowPage))
            {
                target = child;
                break;
            }

            if (child is NavigationPage nav)
            {
                if (ReferenceEquals(nav.CurrentPage, _nowPage) || ReferenceEquals(nav.RootPage, _nowPage))
                {
                    target = nav;
                    break;
                }
            }
        }

        if (target != null)
        {
            tabs.CurrentPage = target;
        }
    }
}
