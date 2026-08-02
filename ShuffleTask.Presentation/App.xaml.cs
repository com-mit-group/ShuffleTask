using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Accessibility;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Presentation;
using ShuffleTask.Presentation.Models;
using ShuffleTask.Presentation.Services;
using ShuffleTask.Presentation.Utilities;
using ShuffleTask.Views;

namespace ShuffleTask;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly IStorageService _storage;
    private readonly ShuffleCoordinatorService _coordinator;
    private readonly TimeProvider _clock;
    private readonly AppSettings _settings;
    private readonly IShuffleLogger? _logger;
    private bool _startupRunning;
    private bool _resumeRunning;

    public OperationState StartupOperationState { get; } = new();

    public App(MainPage mainPage, IStorageService storage, ShuffleCoordinatorService coordinator, TimeProvider clock, AppSettings settings, IShuffleLogger? logger = null)
    {
        InitializeComponent();
        MainPage = mainPage;
        _storage = storage;
        _coordinator = coordinator;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
        StartupOperationState.PropertyChanged += OnStartupOperationStateChanged;
        RequestedThemeChanged += (_, __) => { };
    }

    private void OnStartupOperationStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OperationState.Announcement)
            || string.IsNullOrWhiteSpace(StartupOperationState.Announcement))
        {
            return;
        }

        Dispatcher.Dispatch(() => SemanticScreenReader.Default.Announce(StartupOperationState.Announcement));
    }

    protected override async void OnStart()
    {
        base.OnStart();
        await StartAsync();
    }

    internal async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_startupRunning)
        {
            return;
        }

        _startupRunning = true;
        StartupOperationState.SetLoading("Starting ShuffleTask…");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureSeedDataAsync();
            await PersistedTimerState.RecoverAgainstStorageAsync(_storage, _logger);
            if (_settings.BackgroundActivityEnabled)
            {
                await _coordinator.StartAsync();
            }

            StartupOperationState.SetSuccess("ShuffleTask is ready.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StartupOperationState.SetTransientFailure(
                "Startup was canceled. Retry when you are ready.",
                null,
                StartAsync,
                isBlocking: true);
        }
        catch (Exception ex)
        {
            _logger?.LogOperation(LogLevel.Critical, "ApplicationStartup", "Application startup failed.", ex);
            StartupOperationState.SetFatalFailure(
                "ShuffleTask could not start safely. Check local storage access and retry.",
                null,
                StartAsync);
        }
        finally
        {
            _startupRunning = false;
        }
    }

    protected override async void OnResume()
    {
        base.OnResume();
        await ResumeAsync();
    }

    internal async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (_resumeRunning || !_settings.BackgroundActivityEnabled)
        {
            return;
        }

        _resumeRunning = true;
        StartupOperationState.SetLoading("Restoring ShuffleTask…");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await PersistedTimerState.RecoverAgainstStorageAsync(_storage, _logger);
            await _coordinator.ResumeAsync();
            StartupOperationState.SetSuccess("ShuffleTask restored.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StartupOperationState.SetTransientFailure(
                "Restore was canceled. Retry when you are ready.",
                null,
                ResumeAsync);
        }
        catch (Exception ex)
        {
            _logger?.LogOperation(LogLevel.Error, "ApplicationResume", "Application resume failed.", ex);
            StartupOperationState.SetTransientFailure(
                "ShuffleTask could not restore background activity. Your saved tasks are unchanged.",
                null,
                ResumeAsync);
        }
        finally
        {
            _resumeRunning = false;
        }
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
        _settings.BackgroundActivityEnabled = true;
        _settings.AutoShuffleEnabled = true;
        _settings.ReminderMinutes = 60;
        _settings.MaxDailyShuffles = 6;
        _settings.QuietHoursStart = new TimeSpan(22, 0, 0);
        _settings.QuietHoursEnd = new TimeSpan(7, 0, 0);
        _settings.StreakBias = 0.3;
        _settings.StableRandomnessPerDay = true;
        _settings.Touch(_clock);
        await _storage.SetSettingsAsync(_settings);

        PersistedTimerState.Clear(_logger);
        PersistedSchedulerState.ClearPendingShuffle(_logger);
        PersistedSchedulerState.SaveDailyCount(new DateTimeOffset(nowUtc.Date, TimeSpan.Zero), 0, _logger);
    }
}
