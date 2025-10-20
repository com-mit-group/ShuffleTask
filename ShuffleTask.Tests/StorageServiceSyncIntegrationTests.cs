using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Domain.Events;
using ShuffleTask.Persistence;
using Yaref92.Events;

namespace ShuffleTask.Tests;

[TestFixture]
public sealed class StorageServiceSyncIntegrationTests
{
    [Test]
    public async Task LocalTaskUpsertIsAppliedToPeerStorage()
    {
        var timeA = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var timeB = new FakeTimeProvider(timeA.GetUtcNow());
        (StorageService storageA, string pathA) = await CreateStorageAsync(timeA);
        (StorageService storageB, string pathB) = await CreateStorageAsync(timeB);

        try
        {
            using var harness = LoopbackSyncHarness.Connect(storageA, "device-a", storageB, "device-b");

            var task = new TaskItem { Title = "Peer propagation" };
            await storageA.AddTaskAsync(task).ConfigureAwait(false);

            var replicated = await storageB.GetTaskAsync(task.Id).ConfigureAwait(false);
            Assert.That(replicated, Is.Not.Null);
            Assert.That(replicated!.Title, Is.EqualTo("Peer propagation"));
            Assert.That(replicated.UpdatedAt.Kind, Is.EqualTo(DateTimeKind.Utc));

            timeA.Advance(TimeSpan.FromMinutes(5));
            task.Title = "Updated title";
            await storageA.UpdateTaskAsync(task).ConfigureAwait(false);

            var updated = await storageB.GetTaskAsync(task.Id).ConfigureAwait(false);
            Assert.That(updated, Is.Not.Null);
            Assert.That(updated!.Title, Is.EqualTo("Updated title"));
            Assert.That(updated.UpdatedAt, Is.EqualTo(task.UpdatedAt).Within(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            Cleanup(pathA);
            Cleanup(pathB);
        }
    }

    [Test]
    public async Task LocalTaskDeletionPropagatesToPeerStorage()
    {
        var timeA = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var timeB = new FakeTimeProvider(timeA.GetUtcNow());
        (StorageService storageA, string pathA) = await CreateStorageAsync(timeA);
        (StorageService storageB, string pathB) = await CreateStorageAsync(timeB);

        try
        {
            using var harness = LoopbackSyncHarness.Connect(storageA, "device-primary", storageB, "device-secondary");

            var task = new TaskItem { Title = "To be deleted" };
            await storageA.AddTaskAsync(task).ConfigureAwait(false);

            Assert.That(await storageB.GetTaskAsync(task.Id).ConfigureAwait(false), Is.Not.Null);

            timeA.Advance(TimeSpan.FromMinutes(1));
            await storageA.DeleteTaskAsync(task.Id).ConfigureAwait(false);

            var remoteTask = await storageB.GetTaskAsync(task.Id).ConfigureAwait(false);
            Assert.That(remoteTask, Is.Null);
        }
        finally
        {
            Cleanup(pathA);
            Cleanup(pathB);
        }
    }

    private static async Task<(StorageService storage, string path)> CreateStorageAsync(TimeProvider timeProvider)
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"sync-loop-{Guid.NewGuid():N}.db3");
        var storage = new StorageService(timeProvider, path, logger: null);
        await storage.InitializeAsync().ConfigureAwait(false);
        return (storage, path);
    }

    private static void Cleanup(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class LoopbackSyncHarness : IDisposable
    {
        private readonly LoopbackSyncService _left;
        private readonly LoopbackSyncService _right;

        private LoopbackSyncHarness(LoopbackSyncService left, LoopbackSyncService right)
        {
            _left = left;
            _right = right;
        }

        public static LoopbackSyncHarness Connect(StorageService a, string deviceA, StorageService b, string deviceB)
        {
            var left = new LoopbackSyncService(a, deviceA);
            var right = new LoopbackSyncService(b, deviceB);
            left.Peer = right;
            right.Peer = left;
            a.AttachSyncService(left);
            b.AttachSyncService(right);
            return new LoopbackSyncHarness(left, right);
        }

        public void Dispose()
        {
        }
    }

    private sealed class LoopbackSyncService : IRealtimeSyncService
    {
        private readonly StorageService _storage;
        private readonly string _deviceId;
        private readonly EventAggregator _aggregator = new();
        private int _suppressionCount;

        public LoopbackSyncService(StorageService storage, string deviceId)
        {
            _storage = storage;
            _deviceId = deviceId;
        }

        public LoopbackSyncService? Peer { get; set; }

        public string DeviceId => _deviceId;

        public bool IsConnected => Peer is not null;

        public bool ShouldBroadcastLocalChanges => _suppressionCount == 0;

        public IEventAggregator Aggregator => _aggregator;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : DomainEventBase
        {
            Peer?.Receive(domainEvent);
            return Task.CompletedTask;
        }

        public IDisposable SuppressBroadcast()
        {
            _suppressionCount++;
            return new Scope(this);
        }

        private void Receive(DomainEventBase domainEvent)
        {
            switch (domainEvent)
            {
                case TaskUpserted upserted:
                    ReceiveTaskUpserted(upserted);
                    break;
                case TaskDeleted deleted:
                    ReceiveTaskDeleted(deleted);
                    break;
            }
        }

        private void ReceiveTaskUpserted(TaskUpserted evt)
        {
            if (evt.DeviceId == _deviceId)
            {
                return;
            }

            using (SuppressBroadcast())
            {
                _storage.ApplyRemoteTaskUpsertAsync(evt.Task, evt.UpdatedAt).GetAwaiter().GetResult();
            }
        }

        private void ReceiveTaskDeleted(TaskDeleted evt)
        {
            if (evt.DeviceId == _deviceId)
            {
                return;
            }

            using (SuppressBroadcast())
            {
                _storage.ApplyRemoteDeletionAsync(evt.TaskId, evt.DeletedAt).GetAwaiter().GetResult();
            }
        }

        private void ReleaseSuppression()
        {
            if (_suppressionCount > 0)
            {
                _suppressionCount--;
            }
        }

        private sealed class Scope : IDisposable
        {
            private LoopbackSyncService? _owner;

            public Scope(LoopbackSyncService owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                _owner?.ReleaseSuppression();
                _owner = null;
            }
        }
    }
}
