using NUnit.Framework;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Services;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Tests;

[TestFixture]
public class ImportanceUrgencyCalculatorTests
{
    private static readonly DateTimeOffset DefaultNow = new(2024, 1, 1, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void Calculate_DeadlineOutranksWeeklyRepeat()
    {
        var settings = new AppSettings { StreakBias = 0.3 };

        var deadlineTask = new TaskItem
        {
            Title = "Project report",
            Importance = 5,
            Deadline = DefaultNow.AddHours(24).UtcDateTime,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var repeatingTask = new TaskItem
        {
            Title = "Weekly laundry",
            Importance = 3,
            Repeat = RepeatType.Weekly,
            Weekdays = Weekdays.Mon,
            LastDoneAt = DefaultNow.AddDays(-7).UtcDateTime,
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
            LastDoneAt = DefaultNow.AddHours(-3).UtcDateTime,
            AllowedPeriod = AllowedPeriod.Any
        };

        var deadlineTask = new TaskItem
        {
            Title = "Submit taxes",
            Importance = 3,
            Deadline = DefaultNow.AddHours(6).UtcDateTime,
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
    public void Calculate_LargerTasksEarnSizeMultiplierBoost()
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

        Assert.That(largeScore.SizeMultiplier, Is.GreaterThan(smallScore.SizeMultiplier),
            "Larger tasks should receive the larger size multiplier.");
        Assert.That(largeScore.CombinedScore, Is.GreaterThan(smallScore.CombinedScore),
            "With the same inputs otherwise, the larger task should edge the smaller one.");
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
            Deadline = DefaultNow.AddHours(80).UtcDateTime,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var largeDeadline = new TaskItem
        {
            Title = "Implementation rollout",
            Importance = 3,
            SizePoints = 8,
            Deadline = DefaultNow.AddHours(80).UtcDateTime,
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

    [Test]
    public void Calculate_CanFavorUrgencyThroughSettings()
    {
        var urgencyFavored = new AppSettings
        {
            ImportanceWeight = 0,
            UrgencyWeight = 100,
            UrgencyDeadlineShare = 100,
            RepeatUrgencyPenalty = 1.0
        };

        var importantProject = new TaskItem
        {
            Title = "Strategic plan",
            Importance = 5,
            SizePoints = 3,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var urgentDeadline = new TaskItem
        {
            Title = "Production fix",
            Importance = 1,
            SizePoints = 3,
            Deadline = DefaultNow.AddHours(1).UtcDateTime,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var importantScore = ImportanceUrgencyCalculator.Calculate(importantProject, DefaultNow, urgencyFavored);
        var urgentScore = ImportanceUrgencyCalculator.Calculate(urgentDeadline, DefaultNow, urgencyFavored);

        Assert.That(importantScore.WeightedImportance, Is.EqualTo(0).Within(1e-6),
            "Setting the importance weight to zero should remove the importance contribution.");
        Assert.That(urgentScore.CombinedScore, Is.GreaterThan(importantScore.CombinedScore),
            "With the pool shifted to urgency, imminent deadlines should outscore strategic work.");
    }

    [Test]
    public void Calculate_UrgencyDeadlineShareAdjustsDistribution()
    {
        var settings = new AppSettings
        {
            UrgencyWeight = 40,
            ImportanceWeight = 60,
            UrgencyDeadlineShare = 10,
            RepeatUrgencyPenalty = 1.0
        };

        var repeatingTask = new TaskItem
        {
            Title = "Daily backup",
            Importance = 2,
            Repeat = RepeatType.Daily,
            LastDoneAt = DefaultNow.AddDays(-1).UtcDateTime,
            AllowedPeriod = AllowedPeriod.Any
        };

        var deadlineTask = new TaskItem
        {
            Title = "Status deck",
            Importance = 2,
            Deadline = DefaultNow.AddHours(2).UtcDateTime,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var repeatScore = ImportanceUrgencyCalculator.Calculate(repeatingTask, DefaultNow, settings);
        var deadlineScore = ImportanceUrgencyCalculator.Calculate(deadlineTask, DefaultNow, settings);

        Assert.That(repeatScore.WeightedRepeatUrgency, Is.GreaterThan(deadlineScore.WeightedDeadlineUrgency),
            "Emphasizing repeats should send more of the urgency pool toward repeating work.");
    }

    [Test]
    public void Calculate_SizeBiasStrengthCanBeDisabled()
    {
        var settings = new AppSettings
        {
            SizeBiasStrength = 0.0
        };

        var smallTask = new TaskItem
        {
            Title = "Quick tidy",
            Importance = 3,
            SizePoints = 1,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var largeTask = new TaskItem
        {
            Title = "Major refactor",
            Importance = 3,
            SizePoints = 8,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        var smallScore = ImportanceUrgencyCalculator.Calculate(smallTask, DefaultNow, settings);
        var largeScore = ImportanceUrgencyCalculator.Calculate(largeTask, DefaultNow, settings);

        Assert.That(smallScore.SizeMultiplier, Is.EqualTo(1.0).Within(1e-6),
            "Zeroing the size bias should neutralize the multiplier for small work.");
        Assert.That(largeScore.SizeMultiplier, Is.EqualTo(1.0).Within(1e-6),
            "Zeroing the size bias should neutralize the multiplier for large work as well.");
        Assert.That(smallScore.SizeMultiplier, Is.EqualTo(largeScore.SizeMultiplier).Within(1e-6),
            "With the bias disabled, both sizes should use the same multiplier.");
    }

    [Test]
    public void Calculate_OverdueDeadlineGetsBoostedUrgency()
    {
        var settings = new AppSettings();

        var overdueTask = new TaskItem
        {
            Title = "Catch up on filing",
            Importance = 2,
            Deadline = DefaultNow.AddHours(-5).UtcDateTime,
            Repeat = RepeatType.None,
            AllowedPeriod = AllowedPeriod.Any
        };

        ImportanceUrgencyScore score = ImportanceUrgencyCalculator.Calculate(overdueTask, DefaultNow, settings);

        double expectedBase = 40.0 * 0.75; // default urgency weight * deadline share
        Assert.That(score.WeightedDeadlineUrgency, Is.GreaterThan(expectedBase),
            "Overdue work should receive an urgency boost above the base allocation.");
    }

    [Test]
    public void Calculate_DailyRepeatUrgencyReflectsRecency()
    {
        var settings = new AppSettings
        {
            RepeatUrgencyPenalty = 1.0,
            StreakBias = 0.0
        };

        var recentlyDone = new TaskItem
        {
            Title = "Journal entry",
            Importance = 3,
            Repeat = RepeatType.Daily,
            LastDoneAt = DefaultNow.AddHours(1).UtcDateTime,
            AllowedPeriod = AllowedPeriod.Any
        };

        var overdue = new TaskItem
        {
            Title = "Daily inbox zero",
            Importance = 3,
            Repeat = RepeatType.Daily,
            LastDoneAt = DefaultNow.AddHours(-30).UtcDateTime,
            AllowedPeriod = AllowedPeriod.Any
        };

        ImportanceUrgencyScore recentScore = ImportanceUrgencyCalculator.Calculate(recentlyDone, DefaultNow, settings);
        ImportanceUrgencyScore overdueScore = ImportanceUrgencyCalculator.Calculate(overdue, DefaultNow, settings);

        Assert.That(recentScore.WeightedRepeatUrgency, Is.EqualTo(1.0).Within(1e-6));
        Assert.That(overdueScore.WeightedRepeatUrgency, Is.GreaterThan(recentScore.WeightedRepeatUrgency));
    }

    [Test]
    public void Calculate_IntervalRepeatAccruesOverdueUrgency()
    {
        var settings = new AppSettings
        {
            RepeatUrgencyPenalty = 1.0,
            StreakBias = 0.0
        };

        var intervalTask = new TaskItem
        {
            Title = "Deep system audit",
            Importance = 3,
            Repeat = RepeatType.Interval,
            IntervalDays = 2,
            LastDoneAt = DefaultNow.AddDays(-6).UtcDateTime,
            AllowedPeriod = AllowedPeriod.Any
        };

        ImportanceUrgencyScore score = ImportanceUrgencyCalculator.Calculate(intervalTask, DefaultNow, settings);

        double overdueFloor = 40.0 * 0.25 * 0.6; // base interval urgency when just due
        Assert.That(score.WeightedRepeatUrgency, Is.GreaterThan(overdueFloor));
        Assert.That(score.CombinedScore, Is.GreaterThan(0.0));
    }
}
