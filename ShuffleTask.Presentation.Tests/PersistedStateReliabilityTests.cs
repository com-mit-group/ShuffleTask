using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using NUnit.Framework;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Presentation;
using ShuffleTask.Presentation.Utilities;
using ShuffleTask.Tests.TestDoubles;

namespace ShuffleTask.Presentation.Tests;

[TestFixture]
public class PersistedStateReliabilityTests
{
    [SetUp]
    public void SetUp()
    {
        Preferences.Clear();
#if TEST
        PersistedTimerState.FaultInjector = null;
#endif
    }

    [TearDown]
    public void TearDown()
    {
#if TEST
        PersistedTimerState.FaultInjector = null;
#endif
        Preferences.Clear();
    }

    [Test]
    public void ActiveTimer_SaveAndReload_PreservesCanonicalEnvelopeAndLegacyCompatibility()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var details = new PersistedTimerState.TimerDetails((int)TimerMode.Pomodoro, 0, 2, 4, 25, 5);

        PersistedTimerState.SaveActiveTimer("task-1", 600, expiresAt, details);

        var fromCanonical = PersistedTimerState.TryGetActiveTimer(
            out string taskId,
            out _,
            out bool expired,
            out int durationSeconds,
            out _,
            out PersistedTimerState.TimerDetails? loadedDetails);

        Preferences.Default.Remove(PersistedTimerState.TimerEnvelopeKey);
        var fromLegacy = PersistedTimerState.TryGetActiveTimer(
            out string legacyTaskId,
            out _,
            out _,
            out int legacyDurationSeconds,
            out _);

        Assert.Multiple(() =>
        {
            Assert.That(fromCanonical, Is.True);
            Assert.That(taskId, Is.EqualTo("task-1"));
            Assert.That(expired, Is.False);
            Assert.That(durationSeconds, Is.EqualTo(600));
            Assert.That(loadedDetails, Is.EqualTo(details));
            Assert.That(fromLegacy, Is.True);
            Assert.That(legacyTaskId, Is.EqualTo("task-1"));
            Assert.That(legacyDurationSeconds, Is.EqualTo(600));
        });
    }

    [Test]
    public void ActiveTimer_CorruptEnvelope_QuarantinesAndFallsBackToLegacy()
    {
        var logger = new CapturingShuffleLogger();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        Preferences.Default.Set(PreferenceKeys.CurrentTaskId, "legacy-task");
        Preferences.Default.Set(PreferenceKeys.TimerDurationSeconds, 300);
        Preferences.Default.Set(PreferenceKeys.TimerExpiresAt, expiresAt.ToString("O"));
        Preferences.Default.Set(PersistedTimerState.TimerEnvelopeKey, "{bad json");

        var loaded = PersistedTimerState.TryGetActiveTimer(
            out string taskId,
            out _,
            out _,
            out int durationSeconds,
            out _,
            logger);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.True);
            Assert.That(taskId, Is.EqualTo("legacy-task"));
            Assert.That(durationSeconds, Is.EqualTo(300));
            Assert.That(Preferences.Default.Get(PersistedTimerState.TimerEnvelopeKey, string.Empty), Is.Empty);
            Assert.That(logger.Events.Any(e => e.EventType == "PersistenceQuarantine" && e.Details?.Contains("domain=timer") == true), Is.True);
        });
    }

    [Test]
    public void ActiveTimer_FailedLegacyWrite_KeepsCanonicalRecoverable()
    {
#if TEST
        PersistedTimerState.FaultInjector = checkpoint =>
        {
            if (checkpoint == "timer.after-canonical-write")
            {
                throw new InvalidOperationException("Injected timer failure");
            }
        };
#endif
        Assert.Throws<InvalidOperationException>(() =>
            PersistedTimerState.SaveActiveTimer("task-1", 120, DateTimeOffset.UtcNow.AddMinutes(2)));

#if TEST
        PersistedTimerState.FaultInjector = null;
#endif
        var loaded = PersistedTimerState.TryGetActiveTimer(
            out string taskId,
            out _,
            out _,
            out int durationSeconds,
            out _);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.True);
            Assert.That(taskId, Is.EqualTo("task-1"));
            Assert.That(durationSeconds, Is.EqualTo(120));
        });
    }

    [Test]
    public async Task ActiveTimer_ForCompletedTask_IsQuarantinedDuringStartupRecovery()
    {
        var logger = new CapturingShuffleLogger();
        var storage = new StorageServiceStub();
        await storage.InitializeAsync();
        await storage.AddTaskAsync(new TaskItem { Id = "done-task", Title = "Done", Status = TaskLifecycleStatus.Completed });
        PersistedTimerState.SaveActiveTimer("done-task", 300, DateTimeOffset.UtcNow.AddMinutes(5));

        var recovered = await PersistedTimerState.RecoverAgainstStorageAsync(storage, logger);
        var hasTimer = PersistedTimerState.TryGetActiveTimer(out _, out _, out _, out _, out _);

        Assert.Multiple(() =>
        {
            Assert.That(recovered, Is.False);
            Assert.That(hasTimer, Is.False);
            Assert.That(logger.Events.Any(e => e.EventType == "PersistenceQuarantine" && e.Details?.Contains("completed-task") == true), Is.True);
        });
    }

    [Test]
    public async Task ActiveTimer_ForSnoozedTask_IsQuarantinedDuringStartupRecovery()
    {
        var logger = new CapturingShuffleLogger();
        var storage = new StorageServiceStub();
        await storage.InitializeAsync();
        await storage.AddTaskAsync(new TaskItem
        {
            Id = "snoozed-task",
            Title = "Snoozed",
            Status = TaskLifecycleStatus.Snoozed,
            SnoozedUntil = DateTime.UtcNow.AddHours(1),
            NextEligibleAt = DateTime.UtcNow.AddHours(1)
        });
        PersistedTimerState.SaveActiveTimer("snoozed-task", 300, DateTimeOffset.UtcNow.AddMinutes(5));

        var recovered = await PersistedTimerState.RecoverAgainstStorageAsync(storage, logger);
        var hasTimer = PersistedTimerState.TryGetActiveTimer(out _, out _, out _, out _, out _);

        Assert.Multiple(() =>
        {
            Assert.That(recovered, Is.False);
            Assert.That(hasTimer, Is.False);
            Assert.That(logger.Events.Any(e => e.EventType == "PersistenceQuarantine" && e.Details?.Contains("snoozed-task") == true), Is.True);
        });
    }

    [Test]
    public void SchedulerState_SavePendingAndDailyCount_ReloadsFromCanonicalWhenLegacyKeysAreMissing()
    {
        var scheduledAt = DateTimeOffset.UtcNow.AddMinutes(20);
        var countDate = DateTimeOffset.UtcNow.Date;

        PersistedSchedulerState.SavePendingShuffle("pending-task", scheduledAt);
        PersistedSchedulerState.SaveDailyCount(new DateTimeOffset(countDate, TimeSpan.Zero), 3);
        Preferences.Default.Remove(PreferenceKeys.NextShuffleAt);
        Preferences.Default.Remove(PreferenceKeys.PendingShuffleTaskId);
        Preferences.Default.Remove(PreferenceKeys.ShuffleCountDate);
        Preferences.Default.Remove(PreferenceKeys.ShuffleCount);

        var pending = PersistedSchedulerState.LoadPendingShuffle();
        var daily = PersistedSchedulerState.LoadDailyCount();

        Assert.Multiple(() =>
        {
            Assert.That(pending.NextAt, Is.EqualTo(scheduledAt));
            Assert.That(pending.TaskId, Is.EqualTo("pending-task"));
            Assert.That(daily.Date, Is.EqualTo(new DateTimeOffset(countDate, TimeSpan.Zero)));
            Assert.That(daily.Count, Is.EqualTo(3));
        });
    }

    [Test]
    public void SchedulerState_CorruptEnvelope_QuarantinesAndFallsBackToLegacy()
    {
        var logger = new CapturingShuffleLogger();
        var scheduledAt = DateTimeOffset.UtcNow.AddMinutes(15);
        Preferences.Default.Set(PreferenceKeys.NextShuffleAt, scheduledAt.ToString("O"));
        Preferences.Default.Set(PreferenceKeys.PendingShuffleTaskId, "legacy-pending");
        Preferences.Default.Set(PersistedSchedulerState.SchedulerEnvelopeKey, "{bad json");

        var pending = PersistedSchedulerState.LoadPendingShuffle(logger);

        Assert.Multiple(() =>
        {
            Assert.That(pending.NextAt, Is.EqualTo(scheduledAt));
            Assert.That(pending.TaskId, Is.EqualTo("legacy-pending"));
            Assert.That(Preferences.Default.Get(PersistedSchedulerState.SchedulerEnvelopeKey, string.Empty), Is.Empty);
            Assert.That(logger.Events.Any(e => e.EventType == "PersistenceQuarantine" && e.Details?.Contains("domain=scheduler-state") == true), Is.True);
        });
    }

    private sealed class CapturingShuffleLogger : IShuffleLogger
    {
        public List<(string EventType, string? Details)> Events { get; } = new();

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
            Events.Add((eventType, details));
        }

        public void LogNotification(string notificationType, string title, string? message = null, bool success = true, Exception? exception = null)
        {
        }

        public void LogOperation(LogLevel level, string operation, string? details = null, Exception? exception = null)
        {
        }
    }
}
