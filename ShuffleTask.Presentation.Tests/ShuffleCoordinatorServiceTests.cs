using NSubstitute;
using NUnit.Framework;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Services;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Presentation.Services;
using ShuffleTask.Tests.TestDoubles;
using Microsoft.Maui.Storage;
using System.Threading.Tasks;
using System;

namespace ShuffleTask.Presentation.Tests;

[TestFixture]
public class ShuffleCoordinatorServiceTests
{
    private StorageServiceStub _storage = null!;
    private SchedulerStub _scheduler = null!;
    private NotificationStub _notifications = null!;
    private BackgroundServiceStub _background = null!;
    private AppSettings _settings = null!;
    private TimeProvider _clock = null!;

    [SetUp]
    public async Task SetUp()
    {
        Preferences.Clear();
        _clock = TimeProvider.System;
        _storage = new StorageServiceStub(_clock);
        await _storage.InitializeAsync();
        _scheduler = new SchedulerStub();
        _notifications = new NotificationStub();
        _background = new BackgroundServiceStub();
        _settings = new AppSettings
        {
            Active = true,
            AutoShuffleEnabled = true,
            EnableNotifications = true,
            ReminderMinutes = 15,
            MinGapMinutes = 5,
            MaxDailyShuffles = 3,
        };
    }

    [Test]
    public async Task StartAsync_WhenDailyLimitReached_SchedulesNextDay()
    {
        DateTimeOffset now = _clock.GetUtcNow();
        Preferences.Default.Set(PreferenceKeys.ShuffleCountDate, now.ToString("O"));
        Preferences.Default.Set(PreferenceKeys.ShuffleCount, _settings.MaxDailyShuffles);

        using var service = CreateService();
        await service.StartAsync();

        Assert.That(_background.LastScheduledTaskId, Is.Null, "Daily limit should schedule without task id.");
        Assert.That(_background.LastScheduledAt, Is.GreaterThan(now), "Schedule should set a future time.");
    }

    [Test]
    public async Task StartAsync_WithPendingShuffle_RestoresScheduledTask()
    {
        var now = _clock.GetUtcNow();
        var scheduledAt = now.AddMinutes(30);
        Preferences.Default.Set(PreferenceKeys.NextShuffleAt, scheduledAt.ToString("O"));
        Preferences.Default.Set(PreferenceKeys.PendingShuffleTaskId, "pending");

        await _storage.AddTaskAsync(new TaskItem { Id = "pending", Title = "pending" });

        using var service = CreateService();
        await service.StartAsync();

        Assert.That(_background.LastScheduledTaskId, Is.EqualTo("pending"));
        Assert.That(_background.LastScheduledAt, Is.EqualTo(scheduledAt));
    }

    [Test]
    public async Task StartAsync_WithAvailableTasks_UsesSchedulerCandidate()
    {
        var task = new TaskItem { Id = "candidate", Title = "Candidate" };
        await _storage.AddTaskAsync(task);
        _scheduler.NextCandidate = task;
        _scheduler.NextGapResult = TimeSpan.FromMinutes(10);

        using var service = CreateService();
        await service.StartAsync();

        Assert.That(_background.LastScheduledTaskId, Is.EqualTo(task.Id));
        Assert.That(_background.LastScheduledAt, Is.Not.Null);
    }

    [Test]
    public async Task TimerCallback_ReevaluatesWhenNoTaskId()
    {
        using var service = CreateService();
        await service.StartAsync();

        await _background.TriggerAsyncCallbackAsync();

        Assert.That(_background.AsyncScheduleCount, Is.GreaterThan(0), "Callback should have been wired.");
        Assert.That(_background.ScheduleCount, Is.GreaterThan(1), "Reevaluation should schedule again.");
    }

    private ShuffleCoordinatorService CreateService()
    {
        var network = Substitute.For<INetworkSyncService>();
        return new ShuffleCoordinatorService(
            _storage,
            _scheduler,
            _notifications,
            _settings,
            _clock,
            _background,
            network);
    }

    private sealed class SchedulerStub : ISchedulerService
    {
        public TaskItem? NextCandidate { get; set; }
        public TimeSpan NextGapResult { get; set; } = TimeSpan.FromMinutes(5);

        public TaskItem? PickNextTask(IEnumerable<TaskItem> tasks, AppSettings settings, DateTimeOffset now)
        {
            return NextCandidate;
        }

        public TimeSpan NextGap(AppSettings settings, DateTimeOffset now)
        {
            return NextGapResult;
        }
    }

    private sealed class NotificationStub : INotificationService
    {
        public Task InitializeAsync() => Task.CompletedTask;

        public Task NotifyPhaseAsync(string title, string message, TimeSpan delay, AppSettings settings)
        {
            throw new NotImplementedException();
        }

        public Task NotifyTaskAsync(TaskItem task, int minutes, AppSettings settings) => Task.CompletedTask;

        public Task NotifyTaskAsync(TaskItem task, int minutes, AppSettings settings, TimeSpan delay)
        {
            throw new NotImplementedException();
        }

        public Task ShowToastAsync(string title, string message, AppSettings settings) => Task.CompletedTask;
    }

    private sealed class BackgroundServiceStub : IPersistentBackgroundService
    {
        public int ScheduleCount { get; private set; }
        public int AsyncScheduleCount { get; private set; }
        public DateTimeOffset? LastScheduledAt { get; private set; }
        public string? LastScheduledTaskId { get; private set; }
        private Func<Task>? _callback;

        public Task InitializeAsync() => Task.CompletedTask;

        public void Schedule(DateTimeOffset when, string? taskId)
        {
            ScheduleCount++;
            LastScheduledAt = when;
            LastScheduledTaskId = taskId;
        }

        public Task ScheduleAsync(TimeSpan delay, CancellationToken cancellationToken, Func<Task> callback)
        {
            AsyncScheduleCount++;
            _callback = callback;
            return Task.CompletedTask;
        }

        public void Cancel()
        {
        }

        public Task TriggerAsyncCallbackAsync()
        {
            return _callback?.Invoke() ?? Task.CompletedTask;
        }
    }
}
