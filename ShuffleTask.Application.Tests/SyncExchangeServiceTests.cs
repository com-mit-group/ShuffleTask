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
        var peerA = await CreateHarnessAsync();
        var peerB = await CreateHarnessAsync(CreateTask("only-b", "Remote Only", version: 2, DeviceB));

        var manifestFromB = await peerB.Exchange.BuildManifestAsync(CreateContext("peer-b", DeviceB));
        var requestFromA = await peerA.Exchange.BuildTaskRequestAsync(manifestFromB, CreateContext("peer-a", DeviceA));
        var batchFromB = await peerB.Exchange.BuildTaskBatchAsync(requestFromA, CreateContext("peer-b", DeviceB));

        await peerA.Exchange.ApplyTaskBatchAsync(batchFromB);

        var received = await peerA.Storage.GetTaskAsync("only-b");

        Assert.Multiple(() =>
        {
            Assert.That(requestFromA.RequestedTaskIds, Is.EquivalentTo(new[] { "only-b" }));
            Assert.That(TaskIds(batchFromB), Is.EquivalentTo(new[] { "only-b" }));
            Assert.That(received, Is.Not.Null);
            Assert.That(received!.Title, Is.EqualTo("Remote Only"));
            Assert.That(received.EventVersion, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task BuildTaskBatchAsync_ReturnsExplicitlyRequestedEqualLocalTasks()
    {
        var harness = await CreateHarnessAsync(CreateTask("shared", "Local", version: 3, DeviceB));

        var batch = await harness.Exchange.BuildTaskBatchAsync(CreateRequest("shared"), CreateContext("peer-b", DeviceB));

        Assert.That(TaskIds(batch), Is.EquivalentTo(new[] { "shared" }));
    }

    [Test]
    public async Task BuildLocalTaskBatchAsync_ReturnsTasksMissingFromRemoteManifest()
    {
        var harness = await CreateHarnessAsync(CreateTask("only-b", "Local Only", version: 2, DeviceB));
        var remoteManifest = new SyncManifest("peer-a", UserId, DeviceA, schemaVersion: 1, Array.Empty<SyncManifestEntry>());

        var batch = await harness.Exchange.BuildLocalTaskBatchAsync(remoteManifest, CreateContext("peer-b", DeviceB));

        Assert.That(TaskIds(batch), Is.EquivalentTo(new[] { "only-b" }));
    }

    [Test]
    public async Task ApplyTaskBatchAsync_IgnoresStaleTasks()
    {
        var harness = await CreateHarnessAsync(CreateTask("shared", "Latest", version: 5, DeviceA));

        var staleTask = CreateTask("shared", "Stale", version: 3, DeviceB);
        var batch = new SyncTaskBatch("peer-b", UserId, DeviceB, new[] { staleTask });

        await harness.Exchange.ApplyTaskBatchAsync(batch);

        var stored = await harness.Storage.GetTaskAsync("shared");

        Assert.Multiple(() =>
        {
            Assert.That(stored!.Title, Is.EqualTo("Latest"));
            Assert.That(stored.EventVersion, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task BuildTaskBatchAsync_IgnoresTasksOutsideRequestedUserScope()
    {
        var harness = await CreateHarnessAsync(CreateTask(
            "other-user-task",
            "Other User",
            version: 1,
            DeviceB,
            userId: "user-b"));

        var batch = await harness.Exchange.BuildTaskBatchAsync(
            CreateRequest("other-user-task"),
            CreateContext("peer-b", DeviceB));

        Assert.That(batch.Tasks, Is.Empty);
    }

    private static async Task<ExchangeHarness> CreateHarnessAsync(params TaskItem[] tasks)
    {
        var storage = new InMemoryStorageService();
        await storage.InitializeAsync();

        foreach (var task in tasks)
        {
            await storage.AddTaskAsync(task);
        }

        return new ExchangeHarness(storage, new SyncExchangeService(storage));
    }

    private static SyncTaskRequest CreateRequest(params string[] taskIds)
        => new("peer-a", UserId, DeviceA, taskIds);

    private static SyncPeerContext CreateContext(string peerId, string deviceId)
        => new(peerId, UserId, deviceId);

    private static IEnumerable<string> TaskIds(SyncTaskBatch batch)
        => batch.Tasks.Select(task => task.Id);

    private static TaskItem CreateTask(
        string id,
        string title,
        int version,
        string deviceId,
        string? userId = UserId)
        => new()
        {
            Id = id,
            Title = title,
            UserId = userId,
            DeviceId = deviceId,
            EventVersion = version,
            CreatedAt = BaseTimeUtc.AddMinutes(-version),
            UpdatedAt = BaseTimeUtc.AddMinutes(version),
        };

    private sealed record ExchangeHarness(InMemoryStorageService Storage, SyncExchangeService Exchange);
}
