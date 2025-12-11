using System;
using NUnit.Framework;
using ShuffleTask.Application.Services;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Tests;

[TestFixture]
public class TaskStateValidatorTests
{
    [Test]
    public void IsValidState_ReturnsTrueForWellFormedActiveTask()
    {
        var task = new TaskItem
        {
            Status = TaskLifecycleStatus.Active,
            SnoozedUntil = null,
            CompletedAt = null
        };

        var result = TaskStateValidator.IsValidState(task, DateTimeOffset.UtcNow);

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsValidState_ReturnsFalseWhenActiveTaskHasSnoozeTimestamp()
    {
        var task = new TaskItem
        {
            Status = TaskLifecycleStatus.Active,
            SnoozedUntil = DateTime.UtcNow.AddHours(1)
        };

        var result = TaskStateValidator.IsValidState(task, DateTimeOffset.UtcNow);

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsValidState_ReturnsTrueForSnoozedTasksWithRequiredFields()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem
        {
            Status = TaskLifecycleStatus.Snoozed,
            SnoozedUntil = now.AddMinutes(30).UtcDateTime,
            NextEligibleAt = now.AddMinutes(30).UtcDateTime,
            CompletedAt = null
        };

        var result = TaskStateValidator.IsValidState(task, now);

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsValidState_ReturnsFalseForSnoozedTaskWithoutNextEligible()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem
        {
            Status = TaskLifecycleStatus.Snoozed,
            SnoozedUntil = now.AddMinutes(15).UtcDateTime,
            NextEligibleAt = null
        };

        var result = TaskStateValidator.IsValidState(task, now);

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsValidState_ReturnsFalseWhenSnoozeHasExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem
        {
            Status = TaskLifecycleStatus.Snoozed,
            SnoozedUntil = now.AddMinutes(-10).UtcDateTime,
            NextEligibleAt = now.AddMinutes(-5).UtcDateTime,
            CompletedAt = null
        };

        var result = TaskStateValidator.IsValidState(task, now);

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsValidState_ReturnsTrueForCompletedTasksWithCompletionTimestamp()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem
        {
            Status = TaskLifecycleStatus.Completed,
            CompletedAt = now.UtcDateTime,
            SnoozedUntil = null
        };

        var result = TaskStateValidator.IsValidState(task, now);

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsValidState_AllowsCompletedTasksWithoutNextEligibleTimestamp()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem
        {
            Status = TaskLifecycleStatus.Completed,
            CompletedAt = now.UtcDateTime,
            NextEligibleAt = null,
            SnoozedUntil = null
        };

        var result = TaskStateValidator.IsValidState(task, now);

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsValidState_ReturnsFalseForCompletedTasksMissingCompletionTimestamp()
    {
        var task = new TaskItem
        {
            Status = TaskLifecycleStatus.Completed,
            CompletedAt = null
        };

        var result = TaskStateValidator.IsValidState(task, DateTimeOffset.UtcNow);

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsValidTransition_AllowsOnlyDefinedStateChanges()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TaskStateValidator.IsValidTransition(TaskLifecycleStatus.Active, TaskLifecycleStatus.Snoozed), Is.True);
            Assert.That(TaskStateValidator.IsValidTransition(TaskLifecycleStatus.Active, TaskLifecycleStatus.Completed), Is.True);
            Assert.That(TaskStateValidator.IsValidTransition(TaskLifecycleStatus.Snoozed, TaskLifecycleStatus.Active), Is.True);
            Assert.That(TaskStateValidator.IsValidTransition(TaskLifecycleStatus.Snoozed, TaskLifecycleStatus.Completed), Is.True);
            Assert.That(TaskStateValidator.IsValidTransition(TaskLifecycleStatus.Completed, TaskLifecycleStatus.Active), Is.True);
            Assert.That(TaskStateValidator.IsValidTransition(TaskLifecycleStatus.Completed, TaskLifecycleStatus.Snoozed), Is.False);
            Assert.That(TaskStateValidator.IsValidTransition(TaskLifecycleStatus.Snoozed, TaskLifecycleStatus.Snoozed), Is.False);
        });
    }

    [Test]
    [TestCase(TaskLifecycleStatus.Active, TaskLifecycleStatus.Snoozed, "Task snoozed by user")]
    [TestCase(TaskLifecycleStatus.Active, TaskLifecycleStatus.Completed, "Task completed by user")]
    [TestCase(TaskLifecycleStatus.Snoozed, TaskLifecycleStatus.Active, "Task auto-resumed from snooze")]
    [TestCase(TaskLifecycleStatus.Snoozed, TaskLifecycleStatus.Completed, "Snoozed task completed")]
    [TestCase(TaskLifecycleStatus.Completed, TaskLifecycleStatus.Active, "Repeating task became active")]
    [TestCase(TaskLifecycleStatus.Completed, TaskLifecycleStatus.Completed, "Unknown transition")]
    public void GetTransitionDescription_ReturnsFriendlyExplanation(TaskLifecycleStatus from, TaskLifecycleStatus to, string expected)
    {
        var description = TaskStateValidator.GetTransitionDescription(from, to);

        Assert.That(description, Is.EqualTo(expected));
    }
}

