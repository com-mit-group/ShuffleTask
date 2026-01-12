using NUnit.Framework;
using ShuffleTask.Application.Events;
using ShuffleTask.Application.Exceptions;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Services;
using ShuffleTask.Application.Tests.TestDoubles;
using ShuffleTask.Application.Utilities;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Tests;

public class HandlersAndEventsTests
{
    private static readonly DateTime BaseTimeUtc = new(2024, 2, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task SettingsUpdatedAsyncHandler_UpdatesStorageAndAppSettingsForMatchingUser()
    {
        var appSettings = CreateAuthenticatedSettings("user-1");
        var storageSettings = new AppSettings { FocusMinutes = 10, EventVersion = 1 };
        var storage = new InMemoryStorageService(settings: storageSettings);
        await storage.InitializeAsync();

        var handler = new SettingsUpdatedAsyncHandler(logger: null, storage, appSettings);
        var incomingSettings = new AppSettings { FocusMinutes = 50, EventVersion = 2, UpdatedAt = BaseTimeUtc };
        incomingSettings.Network.UserId = "user-1";
        incomingSettings.Network.AnonymousSession = false;

        var domainEvent = new SettingsUpdatedEvent(incomingSettings, deviceId: "device-a", userId: "user-1");
        await handler.OnNextAsync(domainEvent);

        var stored = await storage.GetSettingsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(stored.FocusMinutes, Is.EqualTo(50));
            Assert.That(appSettings.FocusMinutes, Is.EqualTo(50));
            Assert.That(appSettings.EventVersion, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task SettingsUpdatedAsyncHandler_IgnoresUpdatesForMismatchedUser()
    {
        var appSettings = CreateAuthenticatedSettings("user-1");
        var storageSettings = new AppSettings { FocusMinutes = 10, EventVersion = 4 };
        var storage = new InMemoryStorageService(settings: storageSettings);
        await storage.InitializeAsync();

        var handler = new SettingsUpdatedAsyncHandler(logger: null, storage, appSettings);
        var incomingSettings = new AppSettings { FocusMinutes = 55, EventVersion = 5, UpdatedAt = BaseTimeUtc };
        incomingSettings.Network.UserId = "user-2";
        incomingSettings.Network.AnonymousSession = false;

        var domainEvent = new SettingsUpdatedEvent(incomingSettings, deviceId: "device-a", userId: "user-2");
        await handler.OnNextAsync(domainEvent);

        var stored = await storage.GetSettingsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(stored.FocusMinutes, Is.EqualTo(10));
            Assert.That(appSettings.FocusMinutes, Is.Not.EqualTo(55));
            Assert.That(appSettings.EventVersion, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task TaskUpsertedAsyncHandler_AddsNewTasks()
    {
        var storage = new InMemoryStorageService();
        await storage.InitializeAsync();

        var handler = new TaskUpsertedAsyncHandler(logger: null, storage);
        var incoming = new TaskItem
        {
            Id = "task-1",
            Title = "New",
            EventVersion = 1,
            CreatedAt = BaseTimeUtc.AddMinutes(-10),
            UpdatedAt = BaseTimeUtc
        };

        await handler.OnNextAsync(new TaskUpsertedEvent(incoming, deviceId: "device-a", userId: "user-1"));

        var stored = await storage.GetTaskAsync("task-1");
        Assert.That(stored?.Title, Is.EqualTo("New"));
    }

    [Test]
    public async Task TaskUpsertedAsyncHandler_IgnoresStaleUpdates()
    {
        var storage = new InMemoryStorageService();
        await storage.InitializeAsync();

        await storage.AddTaskAsync(new TaskItem
        {
            Id = "task-1",
            Title = "Existing",
            EventVersion = 5,
            CreatedAt = BaseTimeUtc.AddMinutes(-20),
            UpdatedAt = BaseTimeUtc.AddMinutes(-5)
        });

        var handler = new TaskUpsertedAsyncHandler(logger: null, storage);
        var stale = new TaskItem
        {
            Id = "task-1",
            Title = "Stale",
            EventVersion = 2,
            CreatedAt = BaseTimeUtc.AddMinutes(-30),
            UpdatedAt = BaseTimeUtc.AddMinutes(-10)
        };

        await handler.OnNextAsync(new TaskUpsertedEvent(stale, deviceId: "device-a", userId: "user-1"));

        var stored = await storage.GetTaskAsync("task-1");
        Assert.Multiple(() =>
        {
            Assert.That(stored?.Title, Is.EqualTo("Existing"));
            Assert.That(stored?.EventVersion, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task TaskDeletedAsyncHandler_RemovesTasks()
    {
        var storage = new InMemoryStorageService();
        await storage.InitializeAsync();
        await storage.AddTaskAsync(new TaskItem { Id = "task-1", Title = "Remove" });

        var handler = new TaskDeletedAsyncHandler(logger: null, storage);
        await handler.OnNextAsync(new TaskDeletedEvent("task-1", deviceId: "device-a", userId: "user-1"));

        var stored = await storage.GetTaskAsync("task-1");
        Assert.That(stored, Is.Null);
    }

    [Test]
    public async Task CutInLineUtilities_ClearsOnceModeAndPersistsUpdate()
    {
        var storage = new InMemoryStorageService();
        await storage.InitializeAsync();
        var task = new TaskItem { Id = "task-1", CutInLineMode = CutInLineMode.Once };
        await storage.AddTaskAsync(task);

        var result = await CutInLineUtilities.ClearCutInLineOnceAsync(task, storage);

        var stored = await storage.GetTaskAsync("task-1");
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(task.CutInLineMode, Is.EqualTo(CutInLineMode.None));
            Assert.That(stored?.CutInLineMode, Is.EqualTo(CutInLineMode.None));
        });
    }

    [Test]
    public void EventConstructors_PersistPayloads()
    {
        var task = new TaskItem { Id = "task-1", Title = "Task" };
        var manifest = new[] { new TaskManifestEntry("task-1", 2, BaseTimeUtc) };
        var appSettings = new AppSettings { FocusMinutes = 42 };

        var upserted = new TaskUpsertedEvent(task, deviceId: "device-a", userId: "user-1");
        var deleted = new TaskDeletedEvent("task-2", deviceId: "device-b", userId: "user-2");
        var started = new TaskStarted(deviceId: "device-a", userId: "user-1", taskId: "task-3", minutes: 15);
        var timeUp = new TimeUpNotificationEvent(deviceId: "device-a", userId: "user-1");
        var announced = new TaskManifestAnnounced(manifest, deviceId: "device-a", userId: "user-1");
        var request = new TaskManifestRequest(manifest, deviceId: "device-a", userId: "user-1");
        var batch = new TaskBatchResponse(new[] { task }, deviceId: "device-a", userId: "user-1");
        var settingsUpdated = new SettingsUpdatedEvent(appSettings, deviceId: "device-a", userId: "user-1");

        Assert.Multiple(() =>
        {
            Assert.That(upserted.Task, Is.EqualTo(task));
            Assert.That(deleted.TaskId, Is.EqualTo("task-2"));
            Assert.That(started.TaskId, Is.EqualTo("task-3"));
            Assert.That(started.Minutes, Is.EqualTo(15));
            Assert.That(timeUp.DeviceId, Is.EqualTo("device-a"));
            Assert.That(announced.Manifest?.Count(), Is.EqualTo(1));
            Assert.That(request.Manifest?.Count(), Is.EqualTo(1));
            Assert.That(batch.Tasks?.Count(), Is.EqualTo(1));
            Assert.That(settingsUpdated.Settings.FocusMinutes, Is.EqualTo(42));
        });
    }

    [Test]
    public void NetworkConnectionException_PreservesMessageAndInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var exception = new NetworkConnectionException("outer", inner);

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo("outer"));
            Assert.That(exception.InnerException, Is.EqualTo(inner));
        });
    }

    private static AppSettings CreateAuthenticatedSettings(string userId)
    {
        var settings = new AppSettings();
        settings.Network.AnonymousSession = false;
        settings.Network.UserId = userId;
        return settings;
    }
}
