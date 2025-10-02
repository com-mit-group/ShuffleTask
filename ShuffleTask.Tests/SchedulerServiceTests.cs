using NUnit.Framework;
using ShuffleTask.Application.Abstractions;
using System.Reflection;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Services;
using ShuffleTask.Application.Utilities;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Tests;

[TestFixture]
public class SchedulerServiceTests
{
    private static readonly DateTime DefaultNow = new(2024, 1, 1, 9, 0, 0, DateTimeKind.Local);

    [Test]
    public void PickNextTask_DeterministicSelectsHighestScore()
    {
        var settings = new AppSettings { StreakBias = 0.3, StableRandomnessPerDay = true };
        var scheduler = new SchedulerService(deterministic: true);

        var deadlineTask = new TaskItem
        {
            Id = "A",
            Title = "Prepare slides",
            Importance = 4,
            Deadline = DefaultNow.AddHours(4),
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var routineTask = new TaskItem
        {
            Id = "B",
            Title = "Daily inbox zero",
            Importance = 5,
            Repeat = RepeatType.Daily,
            LastDoneAt = DefaultNow.AddHours(-2),
            AllowedPeriod = AllowedPeriod.Any
        };

        var picked = scheduler.PickNextTask(new[] { deadlineTask, routineTask }, settings, DefaultNow);

        Assert.That(picked, Is.Not.Null);
        Assert.That(picked!.Id, Is.EqualTo("A"),
            "Scheduler should pick the task with the highest combined score when deterministic.");
    }

    [Test]
    public void PickNextTask_DeterministicBreaksTiesByTaskId()
    {
        var settings = new AppSettings { StableRandomnessPerDay = true };
        var scheduler = new SchedulerService(deterministic: true);

        var first = new TaskItem
        {
            Id = "A",
            Title = "Task A",
            Importance = 3,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var second = new TaskItem
        {
            Id = "B",
            Title = "Task B",
            Importance = 3,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var picked = scheduler.PickNextTask(new[] { second, first }, settings, DefaultNow);

        Assert.That(picked, Is.Not.Null);
        Assert.That(picked!.Id, Is.EqualTo("A"),
            "When scores are equal the deterministic scheduler should fall back to sorting by ID.");
    }

    [Test]
    public void PickNextTask_IgnoresPausedAndDisallowedTasks()
    {
        var settings = new AppSettings { StableRandomnessPerDay = true };
        var scheduler = new SchedulerService(deterministic: true);

        var eligible = new TaskItem
        {
            Id = "eligible",
            Title = "Ready task",
            Importance = 3,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var paused = new TaskItem
        {
            Id = "paused",
            Title = "Paused task",
            Importance = 5,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any,
            Paused = true
        };

        var offHours = new TaskItem
        {
            Id = "off",
            Title = "Off-hours task",
            Importance = 5,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.OffWork,
            AutoShuffleAllowed = false
        };

        var picked = scheduler.PickNextTask(new[] { paused, offHours, eligible }, settings, DefaultNow);

        Assert.That(picked, Is.EqualTo(eligible),
            "Scheduler should skip paused tasks and those not allowed at the current time.");
    }

    [Test]
    public void PickNextTask_ReturnsNullWhenNoEligibleTasks()
    {
        var settings = new AppSettings { StableRandomnessPerDay = true };

        var paused = new TaskItem
        {
            Id = "paused",
            Title = "Paused task",
            Importance = 5,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any,
            Paused = true
        };

        var offHours = new TaskItem
        {
            Id = "off",
            Title = "Off-hours task",
            Importance = 5,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.OffWork,
            AutoShuffleAllowed = false
        };

        var picked = SchedulerService.PickNextTask(new[] { paused, offHours }, settings, DefaultNow, deterministic: true);

        Assert.That(picked, Is.Null);
    }

    [Test]
    public void PickNextTask_ReturnsNullWhenTaskCollectionIsNull()
    {
        var settings = new AppSettings();

        TaskItem? picked = SchedulerService.PickNextTask(null!, settings, DefaultNow, deterministic: true);

        Assert.That(picked, Is.Null);
    }

    [Test]
    public void PickNextTask_NonDeterministicPrefersHigherScoreEvenWithJitter()
    {
        var settings = new AppSettings
        {
            StableRandomnessPerDay = false,
            StreakBias = 0.0
        };

        var highPriority = new TaskItem
        {
            Id = "high",
            Title = "Critical bug",
            Importance = 5,
            Deadline = DefaultNow.AddHours(2),
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var lowPriority = new TaskItem
        {
            Id = "low",
            Title = "Inbox grooming",
            Importance = 1,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        TaskItem? picked = SchedulerService.PickNextTask(new[] { lowPriority, highPriority }, settings, DefaultNow, deterministic: false);

        Assert.That(picked, Is.EqualTo(highPriority));
    }

    [Test]
    public void PickNextTask_StableRandomnessProducesConsistentOrderingPerDay()
    {
        var settings = new AppSettings
        {
            StableRandomnessPerDay = true,
            StreakBias = 0.0
        };

        var taskA = new TaskItem
        {
            Id = "A",
            Title = "Even odds A",
            Importance = 3,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var taskB = new TaskItem
        {
            Id = "B",
            Title = "Even odds B",
            Importance = 3,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var now = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        ResetStableSampleSequence();
        TaskItem? firstPick = SchedulerService.PickNextTask(new[] { taskA, taskB }, settings, now, deterministic: false);
        ResetStableSampleSequence();
        TaskItem? repeatPick = SchedulerService.PickNextTask(new[] { taskA, taskB }, settings, now, deterministic: false);

        Assert.Multiple(() =>
        {
            Assert.That(firstPick, Is.Not.Null);
            Assert.That(repeatPick, Is.Not.Null);
            Assert.That(firstPick!.Id, Is.EqualTo(repeatPick!.Id),
                "Resetting the stable sample sequence should reproduce the same ordering for the day.");
        });

        ResetStableSampleSequence();
    }

    private static void ResetStableSampleSequence()
    {
        var type = typeof(UtilityMethods);
        type.GetField("stableSampleSeed", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, int.MinValue);
        type.GetField("stableSampleIndex", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, 0);
    }

    [Test]
    public void NextGap_DeterministicSchedulerReturnsMidpoint()
    {
        var settings = new AppSettings
        {
            MinGapMinutes = -5,
            MaxGapMinutes = 45
        };
        var scheduler = new SchedulerService(deterministic: true);

        TimeSpan result = scheduler.NextGap(settings, DefaultNow);

        // min value is clamped to zero before averaging with max
        Assert.That(result, Is.EqualTo(TimeSpan.FromMinutes(22)));
    }

    [Test]
    public void NextGap_StableRandomnessProducesRepeatableValue()
    {
        var settings = new AppSettings
        {
            MinGapMinutes = 10,
            MaxGapMinutes = 30,
            StableRandomnessPerDay = true
        };
        var scheduler = new SchedulerService();

        TimeSpan first = scheduler.NextGap(settings, DefaultNow);
        TimeSpan second = scheduler.NextGap(settings, DefaultNow.AddMinutes(5));

        Assert.That(first, Is.EqualTo(second));
        Assert.That(first, Is.GreaterThanOrEqualTo(TimeSpan.FromMinutes(10)));
        Assert.That(first, Is.LessThanOrEqualTo(TimeSpan.FromMinutes(30)));
    }

    [Test]
    public void PickNextTask_RespectsAutoShuffleAllowedFlag()
    {
        var settings = new AppSettings { StableRandomnessPerDay = true };
        var scheduler = new SchedulerService(deterministic: true);

        var allowedTask = new TaskItem
        {
            Id = "allowed",
            Title = "Auto-shuffle allowed",
            Importance = 5,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any,
            AutoShuffleAllowed = true
        };

        var disallowedTask = new TaskItem
        {
            Id = "disallowed",
            Title = "Auto-shuffle disallowed",
            Importance = 5,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any,
            AutoShuffleAllowed = false
        };

        var picked = scheduler.PickNextTask(new[] { disallowedTask, allowedTask }, settings, DefaultNow);

        Assert.That(picked, Is.EqualTo(allowedTask),
            "Scheduler should skip tasks with AutoShuffleAllowed = false");
    }

    [Test]
    public void PickNextTask_RespectsCustomTimeWindow()
    {
        var settings = new AppSettings { StableRandomnessPerDay = true };
        var scheduler = new SchedulerService(deterministic: true);

        // DefaultNow is 9:00 AM
        var insideWindow = new TaskItem
        {
            Id = "inside",
            Title = "Inside custom window",
            Importance = 5,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Custom,
            CustomStartTime = new TimeSpan(8, 0, 0),  // 8 AM
            CustomEndTime = new TimeSpan(10, 0, 0)    // 10 AM
        };

        var outsideWindow = new TaskItem
        {
            Id = "outside",
            Title = "Outside custom window",
            Importance = 5,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Custom,
            CustomStartTime = new TimeSpan(14, 0, 0), // 2 PM
            CustomEndTime = new TimeSpan(16, 0, 0)    // 4 PM
        };

        var picked = scheduler.PickNextTask(new[] { outsideWindow, insideWindow }, settings, DefaultNow);

        Assert.That(picked, Is.EqualTo(insideWindow),
            "Scheduler should only pick tasks within their custom time window");
    }
}
