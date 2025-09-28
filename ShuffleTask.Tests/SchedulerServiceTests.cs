using NUnit.Framework;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Services;
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
            AllowedPeriod = AllowedPeriod.Off
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
            AllowedPeriod = AllowedPeriod.Off
        };

        var picked = SchedulerService.PickNextTask(new[] { paused, offHours }, settings, DefaultNow, deterministic: true);

        Assert.That(picked, Is.Null);
    }
}
