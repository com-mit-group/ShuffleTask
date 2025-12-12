using NUnit.Framework;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Services;
using ShuffleTask.Application.Tests.TestDoubles;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Tests;

public class PeerSyncCoordinatorDiffTests
{
    private const string Device999 = "device-999";
    private static readonly DateTime BaseTimeUtc = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task CompareManifestAsync_FavorsHigherVersion()
    {
        var storage = await CreateStorageAsync(new TaskItem
        {
            Id = "shared",
            Title = "Local",
            EventVersion = 1,
            UpdatedAt = BaseTimeUtc,
            CreatedAt = BaseTimeUtc.AddMinutes(-1)
        });

        var coordinator = new PeerSyncCoordinator(storage);
        var manifest = new[] { new TaskManifestEntry("shared", 2, BaseTimeUtc.AddMinutes(-2)) };

        var result = await coordinator.CompareManifestAsync(manifest);

        Assert.That(result.RemoteNewer.Single().TaskId, Is.EqualTo("shared"));
        Assert.That(result.LocalNewer, Is.Empty);
    }

    [Test]
    public async Task CompareManifestAsync_BreaksVersionTiesByUpdatedAt()
    {
        var storage = await CreateStorageAsync(new TaskItem
        {
            Id = "shared",
            Title = "Local",
            EventVersion = 3,
            UpdatedAt = BaseTimeUtc,
            CreatedAt = BaseTimeUtc.AddMinutes(-1)
        });

        var coordinator = new PeerSyncCoordinator(storage);
        var manifest = new[] { new TaskManifestEntry("shared", 3, BaseTimeUtc.AddMinutes(5)) };

        var result = await coordinator.CompareManifestAsync(manifest);

        Assert.That(result.RemoteNewer.Select(e => e.TaskId), Is.EquivalentTo(new[] { "shared" }));
    }

    [Test]
    public async Task CompareManifestAsync_FiltersTasksByUserOwnership()
    {
        var storage = new InMemoryStorageService();
        await storage.InitializeAsync();
        await storage.AddTaskAsync(new TaskItem
        {
            Id = "owned-by-me",
            Title = "Mine",
            UserId = "user-a",
            CreatedAt = BaseTimeUtc,
            UpdatedAt = BaseTimeUtc,
            EventVersion = 1
        });
        var otherUserTask = new TaskItem
        {
            Id = "someone-else",
            Title = "Theirs",
            UserId = "user-b",
            CreatedAt = BaseTimeUtc,
            UpdatedAt = BaseTimeUtc,
            EventVersion = 5
        };
        await storage.AddTaskAsync(otherUserTask);

        var coordinator = new PeerSyncCoordinator(storage);
        var remoteManifest = new[] { TaskManifestEntry.From(otherUserTask) };

        var result = await coordinator.CompareManifestAsync(remoteManifest, userId: "user-a", deviceId: "");

        Assert.That(result.Missing.Select(e => e.TaskId), Is.Empty, "Unowned tasks should be ignored when userId is provided");
        Assert.That(result.RemoteNewer, Is.Empty);
        Assert.That(result.LocalNewer.Select(e => e.TaskId), Is.EquivalentTo(new[] { "owned-by-me" }));
    }

    [Test, Ignore("AI misunderstood and created this bad test. Will need to replace or reinterpret")]
    public async Task CompareManifestAsync_FiltersDeviceScopedTasksWhenAnonymous()
    {
        var storage = new InMemoryStorageService();
        await storage.InitializeAsync();
        await storage.AddTaskAsync(new TaskItem
        {
            Id = "device-task",
            Title = "Device",
            DeviceId = "device-123",
            CreatedAt = BaseTimeUtc,
            UpdatedAt = BaseTimeUtc,
            EventVersion = 2
        });
        await storage.AddTaskAsync(new TaskItem
        {
            Id = "other-device",
            Title = "Other",
            DeviceId = Device999,
            CreatedAt = BaseTimeUtc,
            UpdatedAt = BaseTimeUtc,
            EventVersion = 7
        });

        var coordinator = new PeerSyncCoordinator(storage);
        var remoteManifest = new[] { new TaskManifestEntry("other-device", 10, BaseTimeUtc.AddMinutes(2)) {DeviceId = Device999 } };

        var result = await coordinator.CompareManifestAsync(remoteManifest, userId: "", deviceId: "device-123");

        Assert.That(result.LocalNewer.Select(e => e.TaskId), Is.EquivalentTo(new[] { "device-task" }));
        Assert.That(result.RemoteNewer, Is.Empty);
        Assert.That(result.Missing, Is.Empty, "Tasks for other devices should not be considered");
    }

    private static async Task<InMemoryStorageService> CreateStorageAsync(TaskItem task)
    {
        var storage = new InMemoryStorageService();
        await storage.InitializeAsync();
        await storage.AddTaskAsync(task);
        return storage;
    }
}
