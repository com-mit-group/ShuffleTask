using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Sync;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Domain.Events;
using ShuffleTask.Persistence;
using ShuffleTask.Presentation.Tests.TestSupport;
using ShuffleTask.Tests.TestDoubles;

namespace ShuffleTask.Presentation.Tests;

[TestFixture]
public sealed class RealtimeSyncServiceTests
{
    private string _dbPath = null!;
    private StorageService _storage = null!;
    private FakeTimeProvider _clock = null!;
    private TestNotificationService _notifications = null!;

    [SetUp]
    public async Task SetUp()
    {
        Microsoft.Maui.Storage.Preferences.Reset();
        _clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        _dbPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"sync-{Guid.NewGuid():N}.db3");
        _storage = new StorageService(_clock, _dbPath);
        await _storage.InitializeAsync();
        _notifications = new TestNotificationService();
        Microsoft.Maui.Storage.FileSystem.SetAppDataDirectory(TestContext.CurrentContext.WorkDirectory);
    }

    [TearDown]
    public async Task TearDown()
    {
        await DisposeStorageAsync();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Test]
    public async Task TaskUpsertedSubscriber_AppliesRemoteTask()
    {
        var service = CreateService("device-a");
        var task = new TaskItem
        {
            Id = Guid.NewGuid().ToString("n"),
            Title = "Remote task",
            Description = "Apply from peer",
            Importance = 4,
            UpdatedAt = _clock.GetUtcNow().UtcDateTime
        };

        var evt = new TaskUpserted(TaskItem.Clone(task), "device-b", _clock.GetUtcNow().UtcDateTime);

        await InvokeSubscriberAsync(service, "TaskUpsertedSubscriber", evt);

        var stored = await _storage.GetTaskAsync(task.Id);
        Assert.That(stored, Is.Not.Null, "Remote upsert should persist the task.");
        Assert.That(stored!.Title, Is.EqualTo("Remote task"));
        Assert.That(stored.Description, Is.EqualTo("Apply from peer"));
    }

    [Test]
    public async Task TaskDeletedSubscriber_RemovesRemoteTask()
    {
        var service = CreateService("primary-device");
        var existing = new TaskItem { Title = "Delete me" };
        await _storage.AddTaskAsync(existing);

        var evt = new TaskDeleted(existing.Id, "secondary-device", _clock.GetUtcNow().UtcDateTime);

        await InvokeSubscriberAsync(service, "TaskDeletedSubscriber", evt);

        var stored = await _storage.GetTaskAsync(existing.Id);
        Assert.That(stored, Is.Null, "Remote delete should remove the task from storage.");
    }

    [Test]
    public async Task TaskUpsertedSubscriber_IgnoresLocalDeviceEvent()
    {
        var original = new TaskItem { Title = "Keep original", Description = "baseline" };
        await _storage.AddTaskAsync(original);

        var service = CreateService("device-a");

        var mutated = TaskItem.Clone(original);
        mutated.Title = "Mutated";
        mutated.Description = "Should not apply";
        var evt = new TaskUpserted(mutated, "device-a", _clock.GetUtcNow().UtcDateTime);

        await InvokeSubscriberAsync(service, "TaskUpsertedSubscriber", evt);

        var stored = await _storage.GetTaskAsync(original.Id);
        Assert.That(stored, Is.Not.Null, "Local event should leave the stored task intact.");
        Assert.That(stored!.Title, Is.EqualTo("Keep original"));
        Assert.That(stored.Description, Is.EqualTo("baseline"));
    }

    [Test]
    public async Task TaskDeletedSubscriber_IgnoresLocalDeviceEvent()
    {
        var existing = new TaskItem { Title = "Persist me" };
        await _storage.AddTaskAsync(existing);

        var service = CreateService("device-local");
        var evt = new TaskDeleted(existing.Id, "device-local", _clock.GetUtcNow().UtcDateTime);

        await InvokeSubscriberAsync(service, "TaskDeletedSubscriber", evt);

        var stored = await _storage.GetTaskAsync(existing.Id);
        Assert.That(stored, Is.Not.Null, "Local deletion event should be ignored.");
    }

    [Test]
    public async Task ShuffleStateChangedSubscriber_PersistsPreferences()
    {
        var service = CreateService("pref-device");
        var now = _clock.GetUtcNow();
        var evt = new ShuffleStateChanged(
            new ShuffleStateChanged.ShuffleDeviceContext("remote-device", "task-123", false, trigger: "remote", now.UtcDateTime),
            new ShuffleStateChanged.ShuffleTimerSnapshot(600, now.AddMinutes(10).UtcDateTime, (int)TimerMode.Pomodoro, 1, 2, 4, 20, 5));

        await InvokeSubscriberAsync(service, "ShuffleStateChangedSubscriber", evt);

        Assert.That(Microsoft.Maui.Storage.Preferences.Default.Get(PreferenceKeys.CurrentTaskId, string.Empty), Is.EqualTo("task-123"));
        Assert.That(Microsoft.Maui.Storage.Preferences.Default.Get(PreferenceKeys.TimerMode, -1), Is.EqualTo((int)TimerMode.Pomodoro));
        Assert.That(Microsoft.Maui.Storage.Preferences.Default.Get(PreferenceKeys.PomodoroCycle, -1), Is.EqualTo(2));
        Assert.That(Microsoft.Maui.Storage.Preferences.Default.Get(PreferenceKeys.PomodoroTotal, -1), Is.EqualTo(4));
    }

    [Test]
    public async Task NotificationBroadcastedSubscriber_RelaysToNotificationService()
    {
        var service = CreateService("toast-device");
        var evt = new NotificationBroadcasted(
            new NotificationBroadcasted.NotificationIdentity("notif-1", "peer-device"),
            new NotificationBroadcasted.NotificationContent("Remote", "A reminder"),
            new NotificationBroadcasted.NotificationSchedule(null, _clock.GetUtcNow().UtcDateTime, null),
            isReminder: false);

        await InvokeSubscriberAsync(service, "NotificationBroadcastedSubscriber", evt);

        Assert.That(_notifications.Shown, Has.Count.EqualTo(1), "Notification should be relayed.");
        Assert.That(_notifications.Shown[0].Title, Is.EqualTo("Remote"));
        Assert.That(_notifications.Shown[0].Message, Is.EqualTo("A reminder"));
    }

    [Test]
    public async Task NotificationBroadcastedSubscriber_IgnoresLocalDeviceEvent()
    {
        var service = CreateService("toast-device");
        var evt = new NotificationBroadcasted(
            new NotificationBroadcasted.NotificationIdentity("notif-self", "toast-device"),
            new NotificationBroadcasted.NotificationContent("Local", "Ignore"),
            new NotificationBroadcasted.NotificationSchedule(null, _clock.GetUtcNow().UtcDateTime, null),
            isReminder: false);

        await InvokeSubscriberAsync(service, "NotificationBroadcastedSubscriber", evt);

        Assert.That(_notifications.Shown, Is.Empty, "Self-originated notifications should not be relayed.");
    }

    private RealtimeSyncService CreateService(string deviceId)
    {
        Microsoft.Maui.Storage.Preferences.Default.Set(PreferenceKeys.DeviceId, deviceId);
        return new RealtimeSyncService(_clock, () => _storage, _notifications, new SyncOptions { Enabled = false });
    }

    private static async Task InvokeSubscriberAsync<TEvent>(RealtimeSyncService service, string subscriberName, TEvent evt)
        where TEvent : DomainEventBase
    {
        Type? subscriberType = service.GetType().GetNestedType(subscriberName, BindingFlags.NonPublic);
        Assert.That(subscriberType, Is.Not.Null, $"Subscriber {subscriberName} should exist.");
        object subscriber = Activator.CreateInstance(subscriberType!, service)!;
        MethodInfo? method = subscriberType!.GetMethod("OnNextAsync", BindingFlags.Instance | BindingFlags.Public);
        Assert.That(method, Is.Not.Null, "Subscriber should expose OnNextAsync.");
        Task task = (Task)method!.Invoke(subscriber, new object?[] { evt, CancellationToken.None })!;
        await task.ConfigureAwait(false);
    }

    private async Task DisposeStorageAsync()
    {
        if (_storage is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
    }
}

namespace ShuffleTask.Presentation.Tests.TestSupport;

internal sealed class TestNotificationService : INotificationService
{
    public List<(string Title, string Message)> Shown { get; } = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task NotifyPhaseAsync(string title, string message, TimeSpan delay, AppSettings settings)
        => Task.CompletedTask;

    public Task NotifyTaskAsync(TaskItem task, int minutes, AppSettings settings)
        => Task.CompletedTask;

    public Task NotifyTaskAsync(TaskItem task, int minutes, AppSettings settings, TimeSpan delay)
        => Task.CompletedTask;

    public Task ShowToastAsync(string title, string message, AppSettings settings)
    {
        Shown.Add((title, message));
        return Task.CompletedTask;
    }
}
