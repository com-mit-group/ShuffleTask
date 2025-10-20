using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Sync;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Domain.Events;
using ShuffleTask.Persistence;
using ShuffleTask.Presentation.Tests.TestSupport;
using Yaref92.Events.Abstractions;

namespace ShuffleTask.Presentation.Tests;

[TestFixture]
public sealed class RealtimeSyncIntegrationTests
{
    private FakeTimeProvider _clockA = null!;
    private FakeTimeProvider _clockB = null!;
    private StorageService _storageA = null!;
    private StorageService _storageB = null!;
    private string _dbPathA = null!;
    private string _dbPathB = null!;
    private TestNotificationService _notificationsA = null!;
    private TestNotificationService _notificationsB = null!;

    [SetUp]
    public async Task SetUp()
    {
        Microsoft.Maui.Storage.Preferences.Reset();
        Microsoft.Maui.Storage.FileSystem.SetAppDataDirectory(TestContext.CurrentContext.WorkDirectory);

        _clockA = new FakeTimeProvider(DateTimeOffset.UtcNow);
        _clockB = new FakeTimeProvider(_clockA.GetUtcNow());

        _dbPathA = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"sync-a-{Guid.NewGuid():N}.db3");
        _dbPathB = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"sync-b-{Guid.NewGuid():N}.db3");

        _storageA = new StorageService(_clockA, _dbPathA);
        _storageB = new StorageService(_clockB, _dbPathB);
        await _storageA.InitializeAsync();
        await _storageB.InitializeAsync();

        _notificationsA = new TestNotificationService();
        _notificationsB = new TestNotificationService();
    }

    [TearDown]
    public async Task TearDown()
    {
        await DisposeAsync(_storageA);
        await DisposeAsync(_storageB);

        DeleteIfExists(_dbPathA);
        DeleteIfExists(_dbPathB);
    }

    [Test]
    public async Task TaskUpsert_ReplicatesBetweenRealtimeServices()
    {
        await using var harness = await SyncHarness.CreateAsync(
            _storageA,
            _storageB,
            _clockA,
            _clockB,
            _notificationsA,
            _notificationsB);

        var task = new TaskItem { Title = "Replicate me" };
        await _storageA.AddTaskAsync(task);

        await EventuallyAsync(async () =>
        {
            TaskItem? replicated = await _storageB.GetTaskAsync(task.Id);
            return replicated is not null && replicated.Title == "Replicate me";
        }, TimeSpan.FromSeconds(5), "Remote storage should receive upsert events.");

        _clockA.Advance(TimeSpan.FromMinutes(5));
        task.Title = "Updated";
        await _storageA.UpdateTaskAsync(task);

        await EventuallyAsync(async () =>
        {
            TaskItem? replicated = await _storageB.GetTaskAsync(task.Id);
            return replicated is not null && replicated.Title == "Updated";
        }, TimeSpan.FromSeconds(5), "Remote updates should apply.");
    }

    [Test]
    public async Task TaskDeletion_ReplicatesBetweenRealtimeServices()
    {
        await using var harness = await SyncHarness.CreateAsync(
            _storageA,
            _storageB,
            _clockA,
            _clockB,
            _notificationsA,
            _notificationsB);

        var task = new TaskItem { Title = "Remove me" };
        await _storageA.AddTaskAsync(task);

        await EventuallyAsync(async () => await _storageB.GetTaskAsync(task.Id) is not null, TimeSpan.FromSeconds(5), "Precondition: task should exist remotely.");

        await _storageA.DeleteTaskAsync(task.Id);

        await EventuallyAsync(async () => await _storageB.GetTaskAsync(task.Id) is null, TimeSpan.FromSeconds(5), "Remote storage should reflect deletions.");
    }

    private static async Task DisposeAsync(StorageService storage)
    {
        if (storage is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition, TimeSpan timeout, string failureMessage)
    {
        TimeSpan delay = TimeSpan.FromMilliseconds(50);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(delay).ConfigureAwait(false);
        }

        Assert.Fail(failureMessage);
    }

    private sealed class SyncHarness : IAsyncDisposable
    {
        private readonly RealtimeSyncService _left;
        private readonly RealtimeSyncService _right;
        private readonly Bridge<TaskUpserted> _upserts;
        private readonly Bridge<TaskDeleted> _deletions;

        private SyncHarness(
            RealtimeSyncService left,
            RealtimeSyncService right,
            Bridge<TaskUpserted> upserts,
            Bridge<TaskDeleted> deletions)
        {
            _left = left;
            _right = right;
            _upserts = upserts;
            _deletions = deletions;
        }

        public static async Task<SyncHarness> CreateAsync(
            StorageService leftStorage,
            StorageService rightStorage,
            FakeTimeProvider clockLeft,
            FakeTimeProvider clockRight,
            INotificationService notificationsLeft,
            INotificationService notificationsRight)
        {
            var options = new SyncOptions { Enabled = false };

            Microsoft.Maui.Storage.Preferences.Default.Set(PreferenceKeys.DeviceId, "device-left");
            var left = new RealtimeSyncService(clockLeft, () => leftStorage, notificationsLeft, options);
            await left.InitializeAsync();

            Microsoft.Maui.Storage.Preferences.Default.Set(PreferenceKeys.DeviceId, "device-right");
            var right = new RealtimeSyncService(clockRight, () => rightStorage, notificationsRight, options);
            await right.InitializeAsync();

            var upserts = new Bridge<TaskUpserted>(left.Aggregator, right.Aggregator);
            var deletions = new Bridge<TaskDeleted>(left.Aggregator, right.Aggregator);

            return new SyncHarness(left, right, upserts, deletions);
        }

        public async ValueTask DisposeAsync()
        {
            await _left.DisposeAsync();
            await _right.DisposeAsync();
        }
    }

    private sealed class Bridge<TEvent> : IAsyncEventSubscriber<TEvent> where TEvent : DomainEventBase
    {
        private readonly IEventAggregator _target;

        public Bridge(IEventAggregator source, IEventAggregator target)
        {
            _target = target;
            source.SubscribeToEventType(this);
        }

        public Task OnNextAsync(TEvent @event, CancellationToken cancellationToken = default)
            => _target.PublishEventAsync(@event, cancellationToken);
    }
}
