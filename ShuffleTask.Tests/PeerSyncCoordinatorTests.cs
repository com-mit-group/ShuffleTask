using NUnit.Framework;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Services;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Tests.TestDoubles;
using System;
using System.Linq;

public class PeerSyncCoordinatorTests
{
    private static readonly DateTime BaseTimeUtc = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task CompareManifestAsync_ClassifiesMissingTasks()
    {
        var storage = new StorageServiceStub();
        await storage.InitializeAsync();
        var coordinator = new PeerSyncCoordinator(storage);

        var remoteManifest = new[]
        {
            new TaskManifestEntry("remote-only", 2, BaseTimeUtc.AddMinutes(5))
        };

        var result = await coordinator.CompareManifestAsync(remoteManifest);

        Assert.That(result.Missing.Select(entry => entry.TaskId), Is.EquivalentTo(new[] { "remote-only" }));
        Assert.That(result.GetTasksToRequest(), Is.EquivalentTo(new[] { "remote-only" }));
        Assert.That(result.GetTasksToAdvertise(), Is.Empty);
    }

    [Test]
    public async Task CompareManifestAsync_ClassifiesLocalOnlyAsLocalNewer()
    {
        var storage = new StorageServiceStub();
        await storage.InitializeAsync();

        var localTask = CreateTask("local-only", version: 1, updatedAt: BaseTimeUtc);
        await storage.AddTaskAsync(localTask);

        var coordinator = new PeerSyncCoordinator(storage);
        var result = await coordinator.CompareManifestAsync(Array.Empty<TaskManifestEntry>());

        Assert.That(result.LocalNewer.Select(entry => entry.TaskId), Is.EquivalentTo(new[] { "local-only" }));
        Assert.That(result.GetTasksToRequest(), Is.Empty);
        Assert.That(result.GetTasksToAdvertise(), Is.EquivalentTo(new[] { "local-only" }));
    }

    [Test]
    public async Task CompareManifestAsync_ClassifiesRemoteNewerTasks()
    {
        var storage = new StorageServiceStub();
        await storage.InitializeAsync();

        var localTask = CreateTask("shared", version: 1, updatedAt: BaseTimeUtc);
        await storage.AddTaskAsync(localTask);

        var remoteManifest = new[]
        {
            new TaskManifestEntry("shared", 2, BaseTimeUtc.AddMinutes(10))
        };

        var coordinator = new PeerSyncCoordinator(storage);
        var result = await coordinator.CompareManifestAsync(remoteManifest);

        Assert.That(result.RemoteNewer.Select(entry => entry.TaskId), Is.EquivalentTo(new[] { "shared" }));
        Assert.That(result.GetTasksToRequest(), Is.EquivalentTo(new[] { "shared" }));
        Assert.That(result.GetTasksToAdvertise(), Is.Empty);
    }

    [Test]
    public async Task CompareManifestAsync_ClassifiesLocalNewerTasks()
    {
        var storage = new StorageServiceStub();
        await storage.InitializeAsync();

        var localTask = CreateTask("shared", version: 3, updatedAt: BaseTimeUtc.AddMinutes(10));
        await storage.AddTaskAsync(localTask);

        var remoteManifest = new[]
        {
            new TaskManifestEntry("shared", 2, BaseTimeUtc)
        };

        var coordinator = new PeerSyncCoordinator(storage);
        var result = await coordinator.CompareManifestAsync(remoteManifest);

        Assert.That(result.LocalNewer.Select(entry => entry.TaskId), Is.EquivalentTo(new[] { "shared" }));
        Assert.That(result.GetTasksToRequest(), Is.Empty);
        Assert.That(result.GetTasksToAdvertise(), Is.EquivalentTo(new[] { "shared" }));
    }

    [Test]
    public async Task CompareManifestAsync_UsesUpdatedAtToBreakTies()
    {
        var storage = new StorageServiceStub();
        await storage.InitializeAsync();

        var localTask = CreateTask("shared", version: 2, updatedAt: BaseTimeUtc.AddMinutes(5));
        await storage.AddTaskAsync(localTask);

        var remoteManifest = new[]
        {
            new TaskManifestEntry("shared", 2, BaseTimeUtc.AddMinutes(10))
        };

        var coordinator = new PeerSyncCoordinator(storage);
        var result = await coordinator.CompareManifestAsync(remoteManifest);

        Assert.That(result.RemoteNewer.Select(entry => entry.TaskId), Is.EquivalentTo(new[] { "shared" }));
        Assert.That(result.GetTasksToRequest(), Is.EquivalentTo(new[] { "shared" }));
    }

    [Test]
    public async Task CompareManifestAsync_ClassifiesEqualTasks()
    {
        var storage = new StorageServiceStub();
        await storage.InitializeAsync();

        var localTask = CreateTask("shared", version: 2, updatedAt: BaseTimeUtc);
        await storage.AddTaskAsync(localTask);

        var remoteManifest = new[]
        {
            new TaskManifestEntry("shared", 2, BaseTimeUtc)
        };

        var coordinator = new PeerSyncCoordinator(storage);
        var result = await coordinator.CompareManifestAsync(remoteManifest);

        Assert.That(result.Equal.Select(entry => entry.TaskId), Is.EquivalentTo(new[] { "shared" }));
        Assert.That(result.GetTasksToRequest(), Is.Empty);
        Assert.That(result.GetTasksToAdvertise(), Is.Empty);

    }

    [Test]
    public async Task CompareManifestAsync_ReusesComparisonForSameRemoteAndLocalManifest()
    {
        var storage = new StorageServiceStub();
        await storage.InitializeAsync();

        var remoteManifest = new[]
        {
            new TaskManifestEntry("remote-only", 2, BaseTimeUtc.AddMinutes(5))
        };

        var coordinator = new PeerSyncCoordinator(storage);

        var first = await coordinator.CompareManifestAsync(remoteManifest);
        var second = await coordinator.CompareManifestAsync(remoteManifest);

        Assert.That(second, Is.SameAs(first));
        Assert.That(second.GetTasksToRequest(), Is.EquivalentTo(new[] { "remote-only" }));
        Assert.That(second.GetTasksToAdvertise(), Is.Empty);
        Assert.That(storage.GetTasksCallCount, Is.EqualTo(2));
    }

    [Test]
    public async Task CompareManifestAsync_RecomputesWhenLocalTasksChange()
    {
        var storage = new StorageServiceStub();
        await storage.InitializeAsync();

        var coordinator = new PeerSyncCoordinator(storage);
        var remoteManifest = Array.Empty<TaskManifestEntry>();

        var beforeMutation = await coordinator.CompareManifestAsync(remoteManifest);
        await storage.AddTaskAsync(CreateTask("local-after-first-compare", version: 1, updatedAt: BaseTimeUtc));
        var afterMutation = await coordinator.CompareManifestAsync(remoteManifest);

        Assert.That(beforeMutation.GetTasksToAdvertise(), Is.Empty);
        Assert.That(afterMutation, Is.Not.SameAs(beforeMutation));
        Assert.That(afterMutation.GetTasksToAdvertise(), Is.EquivalentTo(new[] { "local-after-first-compare" }));
    }

    private static TaskItem CreateTask(string id, int version, DateTime updatedAt)
    {
        return new TaskItem
        {
            Id = id,
            Title = "Title",
            CreatedAt = updatedAt.AddMinutes(-1),
            UpdatedAt = updatedAt,
            EventVersion = version
        };
    }
}
