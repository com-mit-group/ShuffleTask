using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Domain.Events;
using ShuffleTask.Presentation.Services;
using ShuffleTask.Presentation.Tests.TestSupport;
using ShuffleTask.Tests.TestDoubles;
using ShuffleTask.ViewModels;
using Yaref92.Events;
using Microsoft.Extensions.Time.Testing;

namespace ShuffleTask.Presentation.Tests;

[TestFixture]
public sealed class DashboardViewModelTests
{
    private FakeTimeProvider _clock = null!;
    private StorageServiceStub _storage = null!;
    private SchedulerStub _scheduler = null!;
    private TestNotificationService _notifications = null!;
    private SyncStub _sync = null!;

    [SetUp]
    public async Task SetUp()
    {
        Microsoft.Maui.Storage.Preferences.Reset();
        _clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        _storage = new StorageServiceStub(_clock);
        await _storage.InitializeAsync();
        _scheduler = new SchedulerStub();
        _notifications = new TestNotificationService();
        _sync = new SyncStub("local-device");
    }

    [Test]
    public async Task OnNextAsync_RemoteActiveState_BindsTaskAndRequestsTimer()
    {
        var task = new TaskItem
        {
            Title = "Remote focus",
            Description = "From peer",
            Status = TaskLifecycleStatus.Active,
            CreatedAt = _clock.GetUtcNow().UtcDateTime
        };
        await _storage.AddTaskAsync(task);

        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        DashboardViewModel.TimerRequest? requested = null;
        viewModel.CountdownRequested += (_, request) => requested = request;

        var now = _clock.GetUtcNow();
        var evt = new ShuffleStateChanged(
            deviceId: "remote-device",
            taskId: task.Id,
            isAutoShuffle: false,
            trigger: "remote",
            eventTimestampUtc: now.UtcDateTime,
            timerDurationSeconds: 300,
            timerExpiresUtc: now.AddSeconds(300).UtcDateTime,
            timerMode: (int)TimerMode.LongInterval,
            pomodoroPhase: null,
            pomodoroCycleIndex: null,
            pomodoroCycleCount: null,
            focusMinutes: null,
            breakMinutes: null);

        await viewModel.OnNextAsync(evt);

        Assert.That(viewModel.ActiveTaskId, Is.EqualTo(task.Id));
        Assert.That(viewModel.Title, Is.EqualTo("Remote focus"));
        Assert.That(viewModel.Description, Is.EqualTo("From peer"));
        Assert.That(viewModel.HasTask, Is.True);
        Assert.That(viewModel.TimerText, Is.EqualTo("05:00"));
        Assert.That(requested, Is.Not.Null, "Remote state should request a timer.");
        Assert.That(requested!.Mode, Is.EqualTo(TimerMode.LongInterval));
        Assert.That(requested.Duration, Is.EqualTo(TimeSpan.FromSeconds(300)));
    }

    [Test]
    public async Task OnNextAsync_RemoteClearedState_ResetsDashboard()
    {
        var task = new TaskItem
        {
            Title = "To clear",
            Description = "Will be cleared",
            Status = TaskLifecycleStatus.Active,
            CreatedAt = _clock.GetUtcNow().UtcDateTime
        };
        await _storage.AddTaskAsync(task);

        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        var now = _clock.GetUtcNow();
        var active = new ShuffleStateChanged(
            deviceId: "remote-device",
            taskId: task.Id,
            isAutoShuffle: false,
            trigger: "remote",
            eventTimestampUtc: now.UtcDateTime,
            timerDurationSeconds: 60,
            timerExpiresUtc: now.AddSeconds(60).UtcDateTime,
            timerMode: (int)TimerMode.LongInterval,
            pomodoroPhase: null,
            pomodoroCycleIndex: null,
            pomodoroCycleCount: null,
            focusMinutes: null,
            breakMinutes: null);
        await viewModel.OnNextAsync(active);

        bool cleared = false;
        viewModel.CountdownCleared += (_, _) => cleared = true;

        var clearedEvent = new ShuffleStateChanged(
            deviceId: "remote-device",
            taskId: null,
            isAutoShuffle: false,
            trigger: "clear",
            eventTimestampUtc: now.AddMinutes(1).UtcDateTime,
            timerDurationSeconds: null,
            timerExpiresUtc: null,
            timerMode: null,
            pomodoroPhase: null,
            pomodoroCycleIndex: null,
            pomodoroCycleCount: null,
            focusMinutes: null,
            breakMinutes: null);

        await viewModel.OnNextAsync(clearedEvent);

        Assert.That(cleared, Is.True, "Clearing state should raise CountdownCleared.");
        Assert.That(viewModel.HasTask, Is.False);
        Assert.That(viewModel.Title, Is.EqualTo("Shuffle a task"));
        Assert.That(viewModel.Description, Is.EqualTo("Tap Shuffle to pick what comes next."));
        Assert.That(viewModel.TimerText, Is.EqualTo("--:--"));
    }

    private DashboardViewModel CreateViewModel()
    {
        var aggregator = new EventAggregator();
        aggregator.RegisterEventType<ShuffleStateChanged>();
        var coordinator = new CoordinatorStub();
        return new DashboardViewModel(_storage, _scheduler, _notifications, coordinator, _clock, aggregator, _sync);
    }

    private sealed class SchedulerStub : ISchedulerService
    {
        public TimeSpan NextGap(AppSettings settings, DateTimeOffset now) => TimeSpan.FromMinutes(1);

        public TaskItem? PickNextTask(IEnumerable<TaskItem> tasks, AppSettings settings, DateTimeOffset now)
            => tasks.FirstOrDefault();
    }

    private sealed class CoordinatorStub : ShuffleCoordinatorService
    {
        public override void RegisterDashboard(DashboardViewModel dashboard)
        {
            // no-op for tests
        }
    }
}
