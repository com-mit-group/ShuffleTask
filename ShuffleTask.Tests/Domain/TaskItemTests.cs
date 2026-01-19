using NUnit.Framework;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Tests.Domain;

[TestFixture]
public class TaskItemTests
{
    [Test]
    public void Clone_Static_Throws_When_Task_Is_Null()
    {
        Assert.Throws<ArgumentNullException>(() => TaskItem.Clone(null!));
    }

    [Test]
    public void Clone_Static_Creates_Distinct_Copy_With_Same_Data()
    {
        var original = CreateTaskItem();

        var clone = TaskItem.Clone(original);

        Assert.That(clone, Is.Not.SameAs(original));
        AssertSameData(original, clone);
    }

    [Test]
    public void Clone_Instance_Creates_Distinct_Copy_With_Same_Data()
    {
        var original = CreateTaskItem();

        var clone = original.Clone();

        Assert.That(clone, Is.Not.SameAs(original));
        AssertSameData(original, clone);
    }

    [Test]
    public void FromData_Throws_When_Source_Is_Null()
    {
        Assert.Throws<ArgumentNullException>(() => TaskItem.FromData(null!));
    }

    [Test]
    public void FromData_Creates_TaskItem_With_Source_Data()
    {
        var source = CreateTaskItem();

        var result = TaskItem.FromData(source);

        Assert.That(result, Is.Not.SameAs(source));
        AssertSameData(source, result);
    }

    [Test]
    public void CopyFrom_Copies_All_Properties()
    {
        var source = CreateTaskItem();
        var target = new TestTaskItemData();

        target.CopyFromPublic(source);

        AssertSameData(source, target);
    }

    [Test]
    public void ScoredTask_Implicit_Conversions_Preserve_Data()
    {
        var task = CreateTaskItem();
        const double score = 42.5;

        ScoredTask scoredTask = (task, score);
        var tuple = ((TaskItem Task, double Score))scoredTask;

        Assert.Multiple(() =>
        {
            Assert.That(tuple.Task, Is.SameAs(task));
            Assert.That(tuple.Score, Is.EqualTo(score));
        });

        var convertedBack = (ScoredTask)tuple;
        Assert.That(convertedBack.Task, Is.SameAs(task));
        Assert.That(convertedBack.Score, Is.EqualTo(score));
    }

    private static TaskItem CreateTaskItem()
    {
        return new TaskItem
        {
            Id = "task-id",
            Title = "Write tests",
            Description = "Add coverage for TaskItem",
            Importance = 3,
            SizePoints = 5.5,
            Deadline = new DateTime(2025, 10, 15, 8, 30, 0, DateTimeKind.Utc),
            Repeat = RepeatType.Daily,
            Weekdays = Weekdays.Mon | Weekdays.Wed,
            IntervalDays = 2,
            LastDoneAt = new DateTime(2025, 9, 10, 12, 0, 0, DateTimeKind.Utc),
            AllowedPeriod = AllowedPeriod.Work,
            CustomWeekdays = Weekdays.Mon | Weekdays.Wed,
            Paused = true,
            CreatedAt = new DateTime(2025, 9, 1, 9, 0, 0, DateTimeKind.Utc),
            Status = TaskLifecycleStatus.Completed,
            SnoozedUntil = new DateTime(2025, 9, 20, 7, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2025, 9, 18, 16, 0, 0, DateTimeKind.Utc),
            NextEligibleAt = new DateTime(2025, 9, 21, 9, 0, 0, DateTimeKind.Utc),
            CustomTimerMode = 1,
            CustomReminderMinutes = 45,
            CustomFocusMinutes = 25,
            CustomBreakMinutes = 5,
            CustomPomodoroCycles = 4,
            CutInLineMode = CutInLineMode.Once,
        };
    }

    private static void AssertSameData(TaskItemData expected, TaskItemData actual)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.Id, Is.EqualTo(expected.Id));
            Assert.That(actual.Title, Is.EqualTo(expected.Title));
            Assert.That(actual.Description, Is.EqualTo(expected.Description));
            Assert.That(actual.Importance, Is.EqualTo(expected.Importance));
            Assert.That(actual.SizePoints, Is.EqualTo(expected.SizePoints));
            Assert.That(actual.Deadline, Is.EqualTo(expected.Deadline));
            Assert.That(actual.Repeat, Is.EqualTo(expected.Repeat));
            Assert.That(actual.Weekdays, Is.EqualTo(expected.Weekdays));
            Assert.That(actual.IntervalDays, Is.EqualTo(expected.IntervalDays));
            Assert.That(actual.LastDoneAt, Is.EqualTo(expected.LastDoneAt));
            Assert.That(actual.AllowedPeriod, Is.EqualTo(expected.AllowedPeriod));
            Assert.That(actual.CustomWeekdays, Is.EqualTo(expected.CustomWeekdays));
            Assert.That(actual.Paused, Is.EqualTo(expected.Paused));
            Assert.That(actual.CreatedAt, Is.EqualTo(expected.CreatedAt));
            Assert.That(actual.Status, Is.EqualTo(expected.Status));
            Assert.That(actual.SnoozedUntil, Is.EqualTo(expected.SnoozedUntil));
            Assert.That(actual.CompletedAt, Is.EqualTo(expected.CompletedAt));
            Assert.That(actual.NextEligibleAt, Is.EqualTo(expected.NextEligibleAt));
            Assert.That(actual.CustomTimerMode, Is.EqualTo(expected.CustomTimerMode));
            Assert.That(actual.CustomReminderMinutes, Is.EqualTo(expected.CustomReminderMinutes));
            Assert.That(actual.CustomFocusMinutes, Is.EqualTo(expected.CustomFocusMinutes));
            Assert.That(actual.CustomBreakMinutes, Is.EqualTo(expected.CustomBreakMinutes));
            Assert.That(actual.CustomPomodoroCycles, Is.EqualTo(expected.CustomPomodoroCycles));
            Assert.That(actual.CutInLineMode, Is.EqualTo(expected.CutInLineMode));
        });
    }

    private sealed class TestTaskItemData : TaskItemData
    {
        public void CopyFromPublic(TaskItemData source)
        {
            CopyFrom(source);
        }
    }
}
