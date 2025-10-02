using NUnit.Framework;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Tests;

[TestFixture]
public class PerTaskTimerOverrideTests
{
    [Test]
    public void TaskItem_CustomTimerFields_DefaultToNull()
    {
        var task = new TaskItem
        {
            Title = "Test Task"
        };

        Assert.Multiple(() =>
        {
            Assert.That(task.CustomTimerMode, Is.Null);
            Assert.That(task.CustomReminderMinutes, Is.Null);
            Assert.That(task.CustomFocusMinutes, Is.Null);
            Assert.That(task.CustomBreakMinutes, Is.Null);
            Assert.That(task.CustomPomodoroCycles, Is.Null);
        });
    }

    [Test]
    public void TaskItem_CustomTimerFields_CanBeSet()
    {
        var task = new TaskItem
        {
            Title = "Test Task",
            CustomTimerMode = 1, // Pomodoro
            CustomReminderMinutes = 45,
            CustomFocusMinutes = 25,
            CustomBreakMinutes = 5,
            CustomPomodoroCycles = 4
        };

        Assert.Multiple(() =>
        {
            Assert.That(task.CustomTimerMode, Is.EqualTo(1));
            Assert.That(task.CustomReminderMinutes, Is.EqualTo(45));
            Assert.That(task.CustomFocusMinutes, Is.EqualTo(25));
            Assert.That(task.CustomBreakMinutes, Is.EqualTo(5));
            Assert.That(task.CustomPomodoroCycles, Is.EqualTo(4));
        });
    }

    [Test]
    public void TaskItem_Clone_PreservesCustomTimerSettings()
    {
        var original = new TaskItem
        {
            Title = "Test Task",
            CustomTimerMode = 1,
            CustomReminderMinutes = 45,
            CustomFocusMinutes = 25,
            CustomBreakMinutes = 5,
            CustomPomodoroCycles = 4
        };

        var clone = TaskItem.Clone(original);

        Assert.Multiple(() =>
        {
            Assert.That(clone.CustomTimerMode, Is.EqualTo(original.CustomTimerMode));
            Assert.That(clone.CustomReminderMinutes, Is.EqualTo(original.CustomReminderMinutes));
            Assert.That(clone.CustomFocusMinutes, Is.EqualTo(original.CustomFocusMinutes));
            Assert.That(clone.CustomBreakMinutes, Is.EqualTo(original.CustomBreakMinutes));
            Assert.That(clone.CustomPomodoroCycles, Is.EqualTo(original.CustomPomodoroCycles));
        });
    }

    [Test]
    public void TaskItem_Clone_PreservesNullCustomTimerSettings()
    {
        var original = new TaskItem
        {
            Title = "Test Task"
        };

        var clone = TaskItem.Clone(original);

        Assert.Multiple(() =>
        {
            Assert.That(clone.CustomTimerMode, Is.Null);
            Assert.That(clone.CustomReminderMinutes, Is.Null);
            Assert.That(clone.CustomFocusMinutes, Is.Null);
            Assert.That(clone.CustomBreakMinutes, Is.Null);
            Assert.That(clone.CustomPomodoroCycles, Is.Null);
        });
    }

    [Test]
    public void TaskItem_MixedCustomSettings_OnlySomeFieldsSet()
    {
        var task = new TaskItem
        {
            Title = "Test Task",
            CustomTimerMode = 0, // LongInterval
            CustomReminderMinutes = 90,
            // Other fields left null
        };

        Assert.Multiple(() =>
        {
            Assert.That(task.CustomTimerMode, Is.EqualTo(0));
            Assert.That(task.CustomReminderMinutes, Is.EqualTo(90));
            Assert.That(task.CustomFocusMinutes, Is.Null);
            Assert.That(task.CustomBreakMinutes, Is.Null);
            Assert.That(task.CustomPomodoroCycles, Is.Null);
        });
    }
}
