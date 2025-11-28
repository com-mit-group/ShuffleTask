using NUnit.Framework;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Services;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Domain.Events;
using ShuffleTask.Persistence;
using ShuffleTask.Tests.TestDoubles;
using Yaref92.Events;
using Yaref92.Events.Abstractions;

namespace ShuffleTask.Tests;

[TestFixture]
public sealed class StorageServiceSyncTests
{
    [Test]
    public async Task AddTaskAsync_BroadcastsTaskUpsertedWhenBroadcastEnabled()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        (StorageService storage, string dbPath) = await CreateStorageAsync(timeProvider);
        try
        {
            var sync = new RecordingSyncService("device-a");
            storage.AttachSyncService(sync);

            var task = new TaskItem
            {
                Title = "Write tests",
                Description = "Verify sync broadcasts",
            };

            await storage.AddTaskAsync(task).ConfigureAwait(false);

            var evt = sync.PublishedEvents.OfType<TaskUpserted>().Single();
            Assert.That(evt.DeviceId, Is.EqualTo("device-a"));
            Assert.That(evt.Task.Id, Is.EqualTo(task.Id));
            Assert.That(evt.Task.Title, Is.EqualTo("Write tests"));
            Assert.That(evt.UpdatedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(evt.UpdatedAt, Is.EqualTo(task.UpdatedAt).Within(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Test]
    public async Task DeleteTaskAsync_BroadcastsTaskDeletedWhenBroadcastEnabled()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        (StorageService storage, string dbPath) = await CreateStorageAsync(timeProvider);
        try
        {
            var sync = new RecordingSyncService("device-z");
            storage.AttachSyncService(sync);

            var task = new TaskItem { Title = "Removable" };
            await storage.AddTaskAsync(task).ConfigureAwait(false);
            sync.PublishedEvents.Clear();

            await storage.DeleteTaskAsync(task.Id).ConfigureAwait(false);

            var evt = sync.PublishedEvents.OfType<TaskDeleted>().Single();
            Assert.That(evt.DeviceId, Is.EqualTo("device-z"));
            Assert.That(evt.TaskId, Is.EqualTo(task.Id));
            Assert.That(evt.DeletedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Test]
    public async Task BroadcastIsSkippedWhenSyncSuppressesLocalChanges()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        (StorageService storage, string dbPath) = await CreateStorageAsync(timeProvider);
        try
        {
            var sync = new RecordingSyncService("device-n", broadcastEnabled: false);
            storage.AttachSyncService(sync);

            var task = new TaskItem { Title = "Silent" };
            await storage.AddTaskAsync(task).ConfigureAwait(false);

            Assert.That(sync.PublishedEvents, Is.Empty);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Test]
    public async Task PublishFailuresAreLoggedAndDoNotThrow()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var logger = new TestLogger();
        (StorageService storage, string dbPath) = await CreateStorageAsync(timeProvider, logger);
        try
        {
            var sync = new RecordingSyncService("device-p", throwOnPublish: true);
            storage.AttachSyncService(sync);

            var task = new TaskItem { Title = "Handle failures" };
            await storage.AddTaskAsync(task).ConfigureAwait(false);

            logger.AssertSyncEventLogged("PublishFailed", task.Id);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static async Task<(StorageService storage, string path)> CreateStorageAsync(TimeProvider timeProvider, IShuffleLogger? logger = null)
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"sync-test-{Guid.NewGuid():N}.db3");
        var storage = new StorageService(timeProvider, path, logger);
        await storage.InitializeAsync().ConfigureAwait(false);
        return (storage, path);
    }

    private static void Cleanup(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            Console.WriteLine("Failed to delete temp test file. Delete manually to save space, or ignore");
        }
    }

    private sealed class RecordingSyncService : IRealtimeSyncService
    {
        private readonly EventAggregator _aggregator = new();
        private readonly string _deviceId;
        private readonly bool _throwOnPublish;
        private int _suppressionCount;

        public RecordingSyncService(string deviceId, bool broadcastEnabled = true, bool throwOnPublish = false)
        {
            _deviceId = deviceId;
            _throwOnPublish = throwOnPublish;
            _suppressionCount = broadcastEnabled ? 0 : 1;
        }

        public List<DomainEventBase> PublishedEvents { get; } = new();

        public string DeviceId => _deviceId;

        public bool IsConnected => true;

        public bool ShouldBroadcastLocalChanges => _suppressionCount == 0;

        public IEventAggregator Aggregator => _aggregator;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : DomainEventBase
        {
            if (_throwOnPublish)
            {
                throw new InvalidOperationException("Simulated failure");
            }

            PublishedEvents.Add(domainEvent);
            return Task.CompletedTask;
        }

        public IDisposable SuppressBroadcast()
        {
            _suppressionCount++;
            return new Scope(this);
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
            private RecordingSyncService _owner;

            public Scope(RecordingSyncService owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                _owner.ReleaseSuppression();
            }
        }
    }

    private sealed class TestLogger : IShuffleLogger
    {
        private readonly List<(string EventType, string? Details)> _syncEvents = new();

        public void LogTaskSelection(string taskId, string taskTitle, string reason, int candidateCount, TimeSpan nextGap)
        {
        }

        public void LogTimerEvent(string eventType, string? taskId = null, TimeSpan? duration = null, string? reason = null)
        {
        }

        public void LogStateTransition(string taskId, string fromStatus, string toStatus, string? reason = null)
        {
        }

        public void LogSyncEvent(string eventType, string? details = null, Exception? exception = null)
        {
            _syncEvents.Add((eventType, details));
        }

        public void LogNotification(string notificationType, string title, string? message = null, bool success = true, Exception? exception = null)
        {
        }

        public void LogOperation(Microsoft.Extensions.Logging.LogLevel level, string operation, string? details = null, Exception? exception = null)
        {
        }

        public void AssertSyncEventLogged(string eventType, string? details)
        {
            Assert.That(_syncEvents, Does.Contain((eventType, details)));
        }
    }
}
