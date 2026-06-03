using NUnit.Framework;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Services;
using ShuffleTask.Application.Tests.TestDoubles;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Tests;

public class SyncExchangeServiceTests
{
    private const string UserId = "user-a";
    private const string DeviceA = "device-a";
    private const string DeviceB = "device-b";
    private static readonly DateTime BaseTimeUtc = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ManifestExchange_RequestsAndReturnsRemoteOnlyTasks()
    {
        var peerAStorage = new InMemoryStorageService();
        await peerAStorage.InitializeAsync();

        var peerBStorage = new InMemoryStorageService();
        await peerBStorage.InitializeAsync();
        await peerBStorage.AddTaskAsync(CreateTask("only-b", "Remote Only", version: 2, DeviceB));

        var peerA = new SyncExchangeService(peerAStorage);
        var peerB = new SyncExchangeService(peerBStorage);

        var manifestFromB = await peerB.BuildManifestAsync(CreateContext("peer-b", DeviceB));
        var requestFromA = await peerA.BuildTaskRequestAsync(manifestFromB, CreateContext("peer-a", DeviceA));
        var batchFromB = await peerB.BuildTaskBatchAsync(requestFromA, CreateContext("peer-b", DeviceB));

        await peerA.ApplyTaskBatchAsync(batchFromB);

        var received = await peerAStorage.GetTaskAsync("only-b");

        Assert.Multiple(() =>
        {
            Assert.That(requestFromA.RequestedTaskIds, Is.EquivalentTo(new[] { "only-b" }));
            Assert.That(batchFromB.Tasks.Select(task => task.Id), Is.EquivalentTo(new[] { "only-b" }));
            Assert.That(received, Is.Not.Null);
            Assert.That(received!.Title, Is.EqualTo("Remote Only"));
            Assert.That(received.EventVersion, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task BuildTaskBatchAsync_ReturnsExplicitlyRequestedEqualLocalTasks()
    {
        var storage = new InMemoryStorageService();
        await storage.InitializeAsync();
        await storage.AddTaskAsync(CreateTask("shared", "Local", version: 3, DeviceB));

        var exchange = new SyncExchangeService(storage);
        var request = new SyncTaskRequest("peer-a", UserId, DeviceA, new[] { "shared" });

        var batch = await exchange.BuildTaskBatchAsync(request, CreateContext("peer-b", DeviceB));

        Assert.That(batch.Tasks.Select(task => task.Id), Is.EquivalentTo(new[] { "shared" }));
    }

    [Test]
    public async Task BuildLocalTaskBatchAsync_ReturnsTasksMissingFromRemoteManifest()
    {
        var storage = new InMemoryStorageService();
        await storage.InitializeAsync();
        await storage.AddTaskAsync(CreateTask("only-b", "Local Only", version: 2, DeviceB));

        var exchange = new SyncExchangeService(storage);
        var remoteManifest = new SyncManifest("peer-a", UserId, DeviceA, schemaVersion: 1, Array.Empty<SyncManifestEntry>());

        var batch = await exchange.BuildLocalTaskBatchAsync(remoteManifest, CreateContext("peer-b", DeviceB));

        Assert.That(batch.Tasks.Select(task => task.Id), Is.EquivalentTo(new[] { "only-b" }));
    }

    [Test]
    public async Task ApplyTaskBatchAsync_IgnoresStaleTasks()
    {
        var storage = new InMemoryStorageService();
        await storage.InitializeAsync();
        await storage.AddTaskAsync(CreateTask("shared", "Latest", version: 5, DeviceA));

        var staleTask = CreateTask("shared", "Stale", version: 3, DeviceB);
        var batch = new SyncTaskBatch("peer-b", UserId, DeviceB, new[] { staleTask });

        var exchange = new SyncExchangeService(storage);
        await exchange.ApplyTaskBatchAsync(batch);

        var stored = await storage.GetTaskAsync("shared");

        Assert.Multiple(() =>
        {
            Assert.That(stored!.Title, Is.EqualTo("Latest"));
            Assert.That(stored.EventVersion, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task BuildTaskBatchAsync_IgnoresTasksOutsideRequestedUserScope()
    {
        var storage = new InMemoryStorageService();
        await storage.InitializeAsync();
        await storage.AddTaskAsync(new TaskItem
        {
            Id = "other-user-task",
            Title = "Other User",
            UserId = "user-b",
            DeviceId = DeviceB,
            EventVersion = 1,
            CreatedAt = BaseTimeUtc,
            UpdatedAt = BaseTimeUtc,
        });

        var exchange = new SyncExchangeService(storage);
        var request = new SyncTaskRequest("peer-a", UserId, DeviceA, new[] { "other-user-task" });

        var batch = await exchange.BuildTaskBatchAsync(request, CreateContext("peer-b", DeviceB));

        Assert.That(batch.Tasks, Is.Empty);
    }

    private static SyncPeerContext CreateContext(string peerId, string deviceId)
        => new(peerId, UserId, deviceId);

    private static TaskItem CreateTask(string id, string title, int version, string deviceId)
        => new()
        {
            Id = id,
            Title = title,
            UserId = UserId,
            DeviceId = deviceId,
            EventVersion = version,
            CreatedAt = BaseTimeUtc.AddMinutes(-version),
            UpdatedAt = BaseTimeUtc.AddMinutes(version),
        };
}
