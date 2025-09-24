using NUnit.Framework;
using ShuffleTask.Models;
using ShuffleTask.Services;

namespace ShuffleTask.Tests;

[TestFixture]
public class ImportanceUrgencyCalculatorTests
{
    private static readonly DateTime DefaultNow = new(2024, 1, 1, 9, 0, 0, DateTimeKind.Local);

    [Test]
    public void Calculate_DeadlineOutranksWeeklyRepeat()
    {
        var settings = new AppSettings { StreakBias = 0.3 };

        var deadlineTask = new TaskItem
        {
            Title = "Project report",
            Importance = 5,
            Deadline = DefaultNow.AddHours(24),
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var repeatingTask = new TaskItem
        {
            Title = "Weekly laundry",
            Importance = 3,
            Repeat = RepeatType.Weekly,
            Weekdays = Weekdays.Mon,
            LastDoneAt = DefaultNow.AddDays(-7),
            AllowedPeriod = AllowedPeriod.Any
        };

        var deadlineScore = ImportanceUrgencyCalculator.Calculate(deadlineTask, DefaultNow, settings);
        var repeatScore = ImportanceUrgencyCalculator.Calculate(repeatingTask, DefaultNow, settings);

        Assert.That(deadlineScore.CombinedScore, Is.GreaterThan(repeatScore.CombinedScore),
            "Deadline-driven work should lead the combined score.");
        Assert.That(deadlineScore.WeightedDeadlineUrgency, Is.GreaterThan(repeatScore.WeightedDeadlineUrgency),
            "Deadline urgency weight should outrank repeating urgency for imminent deadlines.");
    }

    [Test]
    public void Calculate_RepeatingTaskUrgencyIsDampened()
    {
        var settings = new AppSettings { StreakBias = 0.5 };

        var routineTask = new TaskItem
        {
            Title = "Daily stand-up",
            Importance = 4,
            Repeat = RepeatType.Daily,
            LastDoneAt = DefaultNow.AddHours(-3),
            AllowedPeriod = AllowedPeriod.Any
        };

        var deadlineTask = new TaskItem
        {
            Title = "Submit taxes",
            Importance = 3,
            Deadline = DefaultNow.AddHours(6),
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var routineScore = ImportanceUrgencyCalculator.Calculate(routineTask, DefaultNow, settings);
        var deadlineScore = ImportanceUrgencyCalculator.Calculate(deadlineTask, DefaultNow, settings);

        Assert.That(routineScore.WeightedUrgency, Is.LessThan(deadlineScore.WeightedUrgency),
            "Routine work should have less urgency weight than an imminent deadline.");
        Assert.That(deadlineScore.CombinedScore, Is.GreaterThan(routineScore.CombinedScore),
            "Deadline-driven work should lead the combined score.");
    }

    [Test]
    public void Calculate_SmallerTasksEarnSizeMultiplierBoost()
    {
        var settings = new AppSettings();

        var smallTask = new TaskItem
        {
            Title = "Tidy desk",
            Importance = 3,
            SizePoints = 1,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var largeTask = new TaskItem
        {
            Title = "Quarterly plan",
            Importance = 3,
            SizePoints = 8,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var smallScore = ImportanceUrgencyCalculator.Calculate(smallTask, DefaultNow, settings);
        var largeScore = ImportanceUrgencyCalculator.Calculate(largeTask, DefaultNow, settings);

        Assert.That(smallScore.SizeMultiplier, Is.GreaterThan(largeScore.SizeMultiplier),
            "Smaller tasks should receive the larger size multiplier.");
        Assert.That(smallScore.CombinedScore, Is.GreaterThan(largeScore.CombinedScore),
            "With the same inputs otherwise, the smaller task should edge the larger one.");
    }

    [Test]
    public void Calculate_DeadlineUrgencyActivatesEarlierForLargerWork()
    {
        var settings = new AppSettings();

        var smallDeadline = new TaskItem
        {
            Title = "Quick copy edit",
            Importance = 3,
            SizePoints = 1,
            Deadline = DefaultNow.AddHours(80),
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var largeDeadline = new TaskItem
        {
            Title = "Implementation rollout",
            Importance = 3,
            SizePoints = 8,
            Deadline = DefaultNow.AddHours(80),
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var smallScore = ImportanceUrgencyCalculator.Calculate(smallDeadline, DefaultNow, settings);
        var largeScore = ImportanceUrgencyCalculator.Calculate(largeDeadline, DefaultNow, settings);

        Assert.That(largeScore.WeightedDeadlineUrgency, Is.GreaterThan(smallScore.WeightedDeadlineUrgency),
            "Larger work should ramp deadline urgency sooner than small tasks.");
    }

    [Test]
    public void Calculate_HigherImportanceBoostsCombinedScore()
    {
        var settings = new AppSettings();

        var lowImportance = new TaskItem
        {
            Title = "Optional clean-up",
            Importance = 1,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var highImportance = new TaskItem
        {
            Title = "Executive update",
            Importance = 5,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var lowScore = ImportanceUrgencyCalculator.Calculate(lowImportance, DefaultNow, settings);
        var highScore = ImportanceUrgencyCalculator.Calculate(highImportance, DefaultNow, settings);

        Assert.That(highScore.WeightedImportance, Is.GreaterThan(lowScore.WeightedImportance),
            "Importance weighting should grow with the importance value.");
        Assert.That(highScore.CombinedScore, Is.GreaterThan(lowScore.CombinedScore),
            "Higher importance should translate into a larger combined score when all else is equal.");
    }
}
