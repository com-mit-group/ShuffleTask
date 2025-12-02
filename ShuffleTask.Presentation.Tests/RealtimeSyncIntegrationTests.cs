using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Sync;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Domain.Events;
using ShuffleTask.Persistence;
using ShuffleTask.Presentation.Services;
using ShuffleTask.Presentation.Tests.TestSupport;
using ShuffleTask.Tests.TestDoubles;
using Yaref92.Events;
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

        _clockA.AdvanceTime(TimeSpan.FromMinutes(5));
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

    [Test]
    public async Task TcpTransport_ReplicatesBetweenRealtimeServices()
    {
        int portA = GetFreeTcpPort();
        int portB = GetFreeTcpPort();

        var optionsA = new SyncOptions
        {
            Enabled = true,
            ListenPort = portA,
            ReconnectInterval = TimeSpan.FromMilliseconds(1)
        };
        optionsA.Peers.Add(new SyncPeer("127.0.0.1", portB));

        var optionsB = new SyncOptions
        {
            Enabled = true,
            ListenPort = portB,
            ReconnectInterval = TimeSpan.FromMilliseconds(1)
        };
        optionsB.Peers.Add(new SyncPeer("127.0.0.1", portA));

        await using SyncHarness harness = await SyncHarness.CreateAsync(
            _storageA,
            _storageB,
            _clockA,
            _clockB,
            _notificationsA,
            _notificationsB,
            optionsA,
            optionsB);

        await EventuallyAsync(
            () => Task.FromResult(harness.Left.IsConnected && harness.Right.IsConnected),
            TimeSpan.FromSeconds(5),
            "Services should connect over TCP.");

        var task = new TaskItem { Title = "Networked" };
        await _storageA.AddTaskAsync(task);

        await EventuallyAsync(
            async () =>
            {
                TaskItem? replicated = await _storageB.GetTaskAsync(task.Id);
                return replicated is not null && replicated.Title == "Networked";
            },
            TimeSpan.FromSeconds(10),
            "Remote storage should receive network upserts.");

        await _storageA.DeleteTaskAsync(task.Id);

        await EventuallyAsync(
            async () => await _storageB.GetTaskAsync(task.Id) is null,
            TimeSpan.FromSeconds(5),
            "Remote storage should receive network deletions.");
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
        private readonly Bridge<TaskUpserted>? _upserts;
        private readonly Bridge<TaskDeleted>? _deletions;

        private SyncHarness(
            RealtimeSyncService left,
            RealtimeSyncService right,
            Bridge<TaskUpserted>? upserts,
            Bridge<TaskDeleted>? deletions)
        {
            _left = left;
            _right = right;
            _upserts = upserts;
            _deletions = deletions;
        }

        public RealtimeSyncService Left => _left;

        public RealtimeSyncService Right => _right;

        public static async Task<SyncHarness> CreateAsync(
            StorageService leftStorage,
            StorageService rightStorage,
            FakeTimeProvider clockLeft,
            FakeTimeProvider clockRight,
            INotificationService notificationsLeft,
            INotificationService notificationsRight,
            SyncOptions? leftOptions = null,
            SyncOptions? rightOptions = null)
        {
            var optionsLeft = leftOptions ?? new SyncOptions { Enabled = false };
            var optionsRight = rightOptions ?? new SyncOptions { Enabled = false };

            Microsoft.Maui.Storage.Preferences.Default.Set(PreferenceKeys.DeviceId, "device-left");
            var left = new RealtimeSyncService(clockLeft, () => leftStorage, notificationsLeft, optionsLeft);
            await left.InitializeAsync(connectPeers: false);
            TestContext.Progress.WriteLine($"Left listener started: {left.IsListening}");

            Microsoft.Maui.Storage.Preferences.Default.Set(PreferenceKeys.DeviceId, "device-right");
            var right = new RealtimeSyncService(clockRight, () => rightStorage, notificationsRight, optionsRight);
            await right.InitializeAsync(connectPeers: false);
            TestContext.Progress.WriteLine($"Right listener started: {right.IsListening}");

            bool hasTcpPeers = optionsLeft.Enabled && optionsRight.Enabled && (optionsLeft.Peers.Count > 0 || optionsRight.Peers.Count > 0);
            if (hasTcpPeers)
            {
                Assert.That(left.IsListening, Is.True, "Left service should start listening before peer connections.");
                Assert.That(right.IsListening, Is.True, "Right service should start listening before peer connections.");

                await left.InitializeAsync(connectPeers: true);
                await right.InitializeAsync(connectPeers: true);
            }

            Bridge<TaskUpserted>? upserts = null;
            Bridge<TaskDeleted>? deletions = null;

            if (!optionsLeft.Enabled && !optionsRight.Enabled)
            {
                upserts = new Bridge<TaskUpserted>(left.Aggregator, right.Aggregator);
                deletions = new Bridge<TaskDeleted>(left.Aggregator, right.Aggregator);
            }

            return new SyncHarness(left, right, upserts, deletions);
        }

        public async ValueTask DisposeAsync()
        {
            await _left.DisposeAsync();
            await _right.DisposeAsync();
        }
    }

    private sealed class Bridge<TEvent> : IAsyncEventHandler<TEvent> where TEvent : DomainEventBase
    {
        private readonly IEventAggregator _target;

        public Bridge(IEventAggregator source, IEventAggregator target)
        {
            _target = target;
            source.SubscribeToEventType(this);
        }

        public Task OnNextAsync(TEvent domainEvent, CancellationToken cancellationToken = default)
            => _target.PublishEventAsync(domainEvent, cancellationToken);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
