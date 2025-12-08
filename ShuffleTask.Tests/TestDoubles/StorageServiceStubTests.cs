using NUnit.Framework;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Tests.TestDoubles;

namespace ShuffleTask.Tests.TestDoubles;

/// <summary>
/// Tests to validate the StorageServiceStub behaves correctly as a test double
/// </summary>
[TestFixture]
public class StorageServiceStubTests
{
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void AdvanceTime(TimeSpan amount)
        {
            _utcNow = _utcNow.Add(amount);
        }
    }

    [Test]
    public async Task InitializeAsync_CanBeCalledMultipleTimes()
    {
        var stub = new StorageServiceStub();

        await stub.InitializeAsync();
        await stub.InitializeAsync();

        Assert.That(stub.InitializeCallCount, Is.EqualTo(2));
    }

    [Test]
    public async Task AddAndGetTask_RoundTripsTaskCorrectly()
    {
        var stub = new StorageServiceStub();
        await stub.InitializeAsync();

        var task = new TaskItem
        {
            Id = "test-1",
            Title = "Test Task",
            Importance = 3,
            Status = TaskLifecycleStatus.Active
        };

        await stub.AddTaskAsync(task);
        var retrieved = await stub.GetTaskAsync("test-1");

        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Id, Is.EqualTo(task.Id));
        Assert.That(retrieved.Title, Is.EqualTo(task.Title));
        Assert.That(retrieved.Importance, Is.EqualTo(task.Importance));
        Assert.That(retrieved.EventVersion, Is.GreaterThanOrEqualTo(1));
        Assert.That(retrieved.UpdatedAt, Is.Not.EqualTo(default(DateTime)));
    }

    [Test]
    public async Task UpdateTask_ModifiesExistingTask()
    {
        var stub = new StorageServiceStub();
        await stub.InitializeAsync();

        var task = new TaskItem
        {
            Id = "update-test",
            Title = "Original",
            Importance = 2
        };

        await stub.AddTaskAsync(task);

        var original = await stub.GetTaskAsync("update-test");
        var originalVersion = original!.EventVersion;
        var originalUpdatedAt = original.UpdatedAt;

        task.Title = "Updated";
        task.Importance = 4;
        await stub.UpdateTaskAsync(task);

        var retrieved = await stub.GetTaskAsync("update-test");
        Assert.That(retrieved!.Title, Is.EqualTo("Updated"));
        Assert.That(retrieved.Importance, Is.EqualTo(4));
        Assert.That(retrieved.EventVersion, Is.GreaterThan(originalVersion));
        Assert.That(retrieved.UpdatedAt, Is.GreaterThan(originalUpdatedAt));
        Assert.That(stub.UpdateTaskCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DeleteTask_RemovesTaskFromStorage()
    {
        var stub = new StorageServiceStub();
        await stub.InitializeAsync();

        var task = new TaskItem { Id = "delete-me", Title = "Temporary" };
        await stub.AddTaskAsync(task);

        await stub.DeleteTaskAsync("delete-me");

        var retrieved = await stub.GetTaskAsync("delete-me");
        Assert.That(retrieved, Is.Null);
        Assert.That(stub.DeleteTaskCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task MarkTaskDoneAsync_UpdatesStatusAndTimestamps()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero));
        var stub = new StorageServiceStub(clock);
        await stub.InitializeAsync();

        var task = new TaskItem
        {
            Id = "complete-me",
            Title = "Do this",
            Status = TaskLifecycleStatus.Active,
            Repeat = RepeatType.None
        };

        await stub.AddTaskAsync(task);
        var before = await stub.GetTaskAsync("complete-me");
        var completed = await stub.MarkTaskDoneAsync("complete-me");

        Assert.That(completed, Is.Not.Null);
        Assert.That(completed!.Status, Is.EqualTo(TaskLifecycleStatus.Completed));
        Assert.That(completed.CompletedAt, Is.Not.Null);
        Assert.That(completed.LastDoneAt, Is.Not.Null);
        Assert.That(completed.UpdatedAt, Is.EqualTo(completed.CompletedAt));
        Assert.That(completed.EventVersion, Is.GreaterThan(before!.EventVersion));
        Assert.That(stub.MarkDoneCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task SnoozeTaskAsync_UpdatesSnoozedState()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero));
        var stub = new StorageServiceStub(clock);
        await stub.InitializeAsync();

        var task = new TaskItem
        {
            Id = "snooze-me",
            Title = "Later",
            Status = TaskLifecycleStatus.Active
        };

        await stub.AddTaskAsync(task);
        var before = await stub.GetTaskAsync("snooze-me");
        var snoozed = await stub.SnoozeTaskAsync("snooze-me", TimeSpan.FromMinutes(30));

        Assert.That(snoozed, Is.Not.Null);
        Assert.That(snoozed!.Status, Is.EqualTo(TaskLifecycleStatus.Snoozed));
        Assert.That(snoozed.SnoozedUntil, Is.Not.Null);
        Assert.That(snoozed.NextEligibleAt, Is.Not.Null);
        Assert.That(snoozed.UpdatedAt, Is.GreaterThan(before!.UpdatedAt));
        Assert.That(snoozed.EventVersion, Is.GreaterThan(before.EventVersion));
        Assert.That(stub.SnoozeCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ResumeTaskAsync_RestoresActiveState()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero));
        var stub = new StorageServiceStub(clock);
        await stub.InitializeAsync();

        var task = new TaskItem
        {
            Id = "resume-me",
            Title = "Snoozed Task",
            Status = TaskLifecycleStatus.Snoozed,
            SnoozedUntil = clock.GetUtcNow().AddHours(1).UtcDateTime,
            NextEligibleAt = clock.GetUtcNow().AddHours(1).UtcDateTime
        };

        await stub.AddTaskAsync(task);
        var before = await stub.GetTaskAsync("resume-me");
        var resumed = await stub.ResumeTaskAsync("resume-me");

        Assert.That(resumed, Is.Not.Null);
        Assert.That(resumed!.Status, Is.EqualTo(TaskLifecycleStatus.Active));
        Assert.That(resumed.SnoozedUntil, Is.Null);
        Assert.That(resumed.NextEligibleAt, Is.Null);
        Assert.That(resumed.UpdatedAt, Is.GreaterThan(before!.UpdatedAt));
        Assert.That(resumed.EventVersion, Is.GreaterThan(before.EventVersion));
        Assert.That(stub.ResumeCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task GetTasksAsync_AutoResumesDueTasks()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero));
        var stub = new StorageServiceStub(clock);
        await stub.InitializeAsync();

        var snoozedTask = new TaskItem
        {
            Id = "auto-resume",
            Title = "Should resume",
            Status = TaskLifecycleStatus.Snoozed,
            SnoozedUntil = clock.GetUtcNow().AddMinutes(-5).UtcDateTime,
            NextEligibleAt = clock.GetUtcNow().AddMinutes(-5).UtcDateTime
        };

        await stub.AddTaskAsync(snoozedTask);
        var before = await stub.GetTaskAsync("auto-resume");

        // Advance time past the snooze period
        clock.AdvanceTime(TimeSpan.FromMinutes(10));

        var tasks = await stub.GetTasksAsync();

        Assert.That(tasks.Count, Is.EqualTo(1));
        Assert.That(tasks[0].Status, Is.EqualTo(TaskLifecycleStatus.Active));
        Assert.That(tasks[0].UpdatedAt, Is.GreaterThan(before!.UpdatedAt));
        Assert.That(tasks[0].EventVersion, Is.GreaterThan(before.EventVersion));
    }

    [Test]
    public async Task SettingsRoundTrip_PreservesAllProperties()
    {
        var stub = new StorageServiceStub();
        await stub.InitializeAsync();

        var settings = new AppSettings
        {
            WorkStart = new TimeSpan(9, 0, 0),
            WorkEnd = new TimeSpan(17, 0, 0),
            MinGapMinutes = 10,
            MaxGapMinutes = 30,
            ReminderMinutes = 5,
            EnableNotifications = true,
            SoundOn = false,
            Active = true,
            StreakBias = 0.5,
            StableRandomnessPerDay = true,
            ImportanceWeight = 60,
            UrgencyWeight = 40,
            UrgencyDeadlineShare = 70,
            RepeatUrgencyPenalty = 0.8,
            SizeBiasStrength = 0.3
        };

        await stub.SetSettingsAsync(settings);
        var retrieved = await stub.GetSettingsAsync();

        Assert.That(retrieved.WorkStart, Is.EqualTo(settings.WorkStart));
        Assert.That(retrieved.WorkEnd, Is.EqualTo(settings.WorkEnd));
        Assert.That(retrieved.MinGapMinutes, Is.EqualTo(settings.MinGapMinutes));
        Assert.That(retrieved.MaxGapMinutes, Is.EqualTo(settings.MaxGapMinutes));
        Assert.That(retrieved.ReminderMinutes, Is.EqualTo(settings.ReminderMinutes));
        Assert.That(retrieved.EnableNotifications, Is.EqualTo(settings.EnableNotifications));
        Assert.That(retrieved.SoundOn, Is.EqualTo(settings.SoundOn));
        Assert.That(retrieved.Active, Is.EqualTo(settings.Active));
        Assert.That(retrieved.StreakBias, Is.EqualTo(settings.StreakBias));
        Assert.That(retrieved.StableRandomnessPerDay, Is.EqualTo(settings.StableRandomnessPerDay));
        Assert.That(stub.GetSettingsCallCount, Is.EqualTo(1));
        Assert.That(stub.SetSettingsCallCount, Is.EqualTo(1));
    }
}
