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
        var task = new TaskItem
        {
            Status = TaskLifecycleStatus.Snoozed,
            SnoozedUntil = DateTime.UtcNow.AddMinutes(30),
            NextEligibleAt = DateTime.UtcNow.AddMinutes(30),
            CompletedAt = null
        };

        var result = TaskStateValidator.IsValidState(task, DateTimeOffset.UtcNow);

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsValidState_ReturnsFalseForSnoozedTaskWithoutNextEligible()
    {
        var task = new TaskItem
        {
            Status = TaskLifecycleStatus.Snoozed,
            SnoozedUntil = DateTime.UtcNow.AddMinutes(15),
            NextEligibleAt = null
        };

        var result = TaskStateValidator.IsValidState(task, DateTimeOffset.UtcNow);

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsValidState_ReturnsTrueForCompletedTasksWithCompletionTimestamp()
    {
        var task = new TaskItem
        {
            Status = TaskLifecycleStatus.Completed,
            CompletedAt = DateTime.UtcNow,
            SnoozedUntil = null
        };

        var result = TaskStateValidator.IsValidState(task, DateTimeOffset.UtcNow);

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
