using NUnit.Framework;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Events;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Services;
using ShuffleTask.Application.Tests.TestDoubles;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Tests;

public class NetworkSyncIntegrationTests
{
    private const string UserId = "user-a";
    private const string DeviceA = "device-a";
    private const string DeviceB = "device-b";

    [Test]
    public async Task PeersRequestMissingTasksAndIgnoreStaleUpserts()
    {
        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var peerAStorage = new InMemoryStorageService();
        await peerAStorage.InitializeAsync();
        await peerAStorage.AddTaskAsync(new TaskItem
        {
            Id = "shared",
            Title = "Shared Latest",
            UserId = UserId,
            DeviceId = DeviceA,
            EventVersion = 5,
            CreatedAt = baseTime.AddHours(-2),
            UpdatedAt = baseTime.AddMinutes(10)
        });
        await peerAStorage.AddTaskAsync(new TaskItem
        {
            Id = "only-a",
            Title = "Local Only",
            UserId = UserId,
            DeviceId = DeviceA,
            EventVersion = 2,
            CreatedAt = baseTime,
            UpdatedAt = baseTime.AddMinutes(1)
        });

        var peerBStorage = new InMemoryStorageService();
        await peerBStorage.InitializeAsync();
        await peerBStorage.AddTaskAsync(new TaskItem
        {
            Id = "shared",
            Title = "Shared Old",
            UserId = UserId,
            DeviceId = DeviceB,
            EventVersion = 3,
            CreatedAt = baseTime.AddHours(-3),
            UpdatedAt = baseTime.AddMinutes(5)
        });
        await peerBStorage.AddTaskAsync(new TaskItem
        {
            Id = "only-b",
            Title = "Remote Only",
            UserId = UserId,
            DeviceId = DeviceB,
            EventVersion = 1,
            CreatedAt = baseTime,
            UpdatedAt = baseTime.AddMinutes(2)
        });

        var peerACoordinator = new PeerSyncCoordinator(peerAStorage);
        var peerBCoordinator = new PeerSyncCoordinator(peerBStorage);
        var peerAHandler = new TaskUpsertedAsyncHandler(logger: null, peerAStorage);
        var peerBHandler = new TaskUpsertedAsyncHandler(logger: null, peerBStorage);

        var manifestFromB = await BuildManifestAsync(peerBStorage);
        var manifestFromA = await BuildManifestAsync(peerAStorage);

        var comparisonAtA = await peerACoordinator.CompareManifestAsync(manifestFromB, UserId, DeviceA);
        var comparisonAtB = await peerBCoordinator.CompareManifestAsync(manifestFromA, UserId, DeviceB);

        // Peer A requests what it is missing/newer on peer B
        var tasksRequestedByA = comparisonAtA.GetTasksToRequest();
        await SendTasksAsync(peerBStorage, peerAHandler, tasksRequestedByA);

        // Peer B requests what it is missing/newer on peer A
        var tasksRequestedByB = comparisonAtB.GetTasksToRequest();
        await SendTasksAsync(peerAStorage, peerBHandler, tasksRequestedByB);

        // Peer B accidentally re-sends a stale shared task; it should be ignored by peer A
        var staleShared = new TaskUpsertedEvent(new TaskItem
        {
            Id = "shared",
            Title = "Shared Old",
            UserId = UserId,
            DeviceId = DeviceB,
            EventVersion = 2,
            CreatedAt = baseTime.AddHours(-3),
            UpdatedAt = baseTime.AddMinutes(4)
        }, deviceId: DeviceB, userId: UserId);
        await peerAHandler.OnNextAsync(staleShared);

        var finalSharedA = await peerAStorage.GetTaskAsync("shared");
        var finalSharedB = await peerBStorage.GetTaskAsync("shared");
        var finalOnlyAOnB = await peerBStorage.GetTaskAsync("only-a");
        var finalOnlyBOnA = await peerAStorage.GetTaskAsync("only-b");

        Assert.Multiple(() =>
        {
            Assert.That(finalSharedA!.EventVersion, Is.EqualTo(5), "Stale shared task should not overwrite local newer version");
            Assert.That(finalSharedB!.EventVersion, Is.EqualTo(5), "Peer B should receive the latest shared task version");
            Assert.That(finalOnlyAOnB, Is.Not.Null, "Peer B should receive peer A's exclusive task");
            Assert.That(finalOnlyBOnA, Is.Not.Null, "Peer A should receive peer B's exclusive task");
        });
    }

    private static async Task SendTasksAsync(IStorageService senderStorage, TaskUpsertedAsyncHandler receiverHandler, IEnumerable<string> taskIds)
    {
        foreach (var taskId in taskIds)
        {
            var task = await senderStorage.GetTaskAsync(taskId).ConfigureAwait(false);
            if (task is null)
            {
                continue;
            }

            var evt = new TaskUpsertedEvent(task, task.DeviceId ?? string.Empty, task.UserId);
            await receiverHandler.OnNextAsync(evt).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyCollection<TaskManifestEntry>> BuildManifestAsync(IStorageService storage)
    {
        var tasks = await storage.GetTasksAsync(UserId, string.Empty).ConfigureAwait(false);
        return tasks.Select(t => new TaskManifestEntry(t.Id, t.EventVersion, t.UpdatedAt)
        {
            DeviceId = t.DeviceId,
            UserId = t.UserId
        }).ToArray();
    }
}
