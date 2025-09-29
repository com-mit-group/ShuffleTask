using NUnit.Framework;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Utilities;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Tests;

[TestFixture]
public class UtilityMethodsTests
{
    private static DateTimeOffset LocalDate(int year, int month, int day, int hour = 0, int minute = 0)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local);
        return new DateTimeOffset(local);
    }

    [Test]
    public void Clamp01_ClampsValuesOutsideRange()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UtilityMethods.Clamp01(-1.2), Is.Zero);
            Assert.That(UtilityMethods.Clamp01(0.42), Is.EqualTo(0.42));
            Assert.That(UtilityMethods.Clamp01(4.2), Is.EqualTo(1.0));
        });
    }

    [Test]
    public void EnsureUtc_ConvertsNullableDateTimes()
    {
        DateTime? nullValue = null;
        Assert.That(UtilityMethods.EnsureUtc(nullValue), Is.Null);

        var local = new DateTime(2024, 1, 1, 9, 0, 0, DateTimeKind.Local);
        var unspecified = new DateTime(2024, 1, 1, 9, 0, 0, DateTimeKind.Unspecified);
        var utc = new DateTime(2024, 1, 1, 9, 0, 0, DateTimeKind.Utc);

        Assert.Multiple(() =>
        {
            Assert.That(UtilityMethods.EnsureUtc(local)!.Value.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(UtilityMethods.EnsureUtc(unspecified)!.Value.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(UtilityMethods.EnsureUtc(utc)!.Value.Offset, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void ExponentArray_NormalizesByMaximumScore()
    {
        var low = new TaskItem { Id = "A" };
        var high = new TaskItem { Id = "B" };
        var scored = new List<ScoredTask>
        {
            new(low, 1.0),
            new(high, 11.0)
        };

        double[] exp = UtilityMethods.ExponentArray(scored);

        Assert.That(exp[1], Is.EqualTo(1.0));
        Assert.That(exp[0], Is.EqualTo(Math.Exp(-10.0))); // high score treated as baseline
    }

    [Test]
    public void LifecycleEligible_HonorsTaskLifecycleStates()
    {
        var now = LocalDate(2024, 1, 1, 10, 0).UtcDateTime;
        var active = new TaskItem { Status = TaskLifecycleStatus.Active };
        var snoozedNoDate = new TaskItem { Status = TaskLifecycleStatus.Snoozed };
        var snoozedPast = new TaskItem
        {
            Status = TaskLifecycleStatus.Snoozed,
            NextEligibleAt = LocalDate(2023, 12, 31, 23, 0).DateTime
        };
        var completedFuture = new TaskItem
        {
            Status = TaskLifecycleStatus.Completed,
            NextEligibleAt = LocalDate(2024, 1, 2, 9, 0).DateTime
        };

        Assert.Multiple(() =>
        {
            Assert.That(UtilityMethods.LifecycleEligible(active, now), Is.True);
            Assert.That(UtilityMethods.LifecycleEligible(snoozedNoDate, now), Is.False);
            Assert.That(UtilityMethods.LifecycleEligible(snoozedPast, now), Is.True);
            Assert.That(UtilityMethods.LifecycleEligible(completedFuture, now), Is.False);
        });
    }

    [Test]
    public void DeterministicMaxScoredTask_OrdersByScoreThenId()
    {
        var scored = new List<ScoredTask>
        {
            new(new TaskItem { Id = "B" }, 0.8),
            new(new TaskItem { Id = "A" }, 0.8),
            new(new TaskItem { Id = "C" }, 0.7)
        };

        TaskItem result = UtilityMethods.DeterministicMaxScoredTask(scored);

        Assert.That(result.Id, Is.EqualTo("A"));
    }

    [Test]
    public void CreateRng_StableRandomnessProducesRepeatableSequence()
    {
        var settings = new AppSettings { StableRandomnessPerDay = true };
        var task = new TaskItem { Id = "repeatable" };
        var now = LocalDate(2024, 1, 1, 8, 0);

        Random first = UtilityMethods.CreateRng(settings, now, task);
        Random second = UtilityMethods.CreateRng(settings, now, task);

        Assert.That(second.NextDouble(), Is.EqualTo(first.NextDouble()));
    }

    [Test]
    public void NextStableSample_ResetsSequencePerDay()
    {
        var dayOne = LocalDate(2024, 1, 1, 9, 0);
        var dayTwo = LocalDate(2024, 1, 2, 9, 0);

        double firstSample = UtilityMethods.NextStableSample(dayOne);
        double secondSample = UtilityMethods.NextStableSample(dayOne.AddMinutes(1));

        double nextDaySample = UtilityMethods.NextStableSample(dayTwo);
        double resetSample = UtilityMethods.NextStableSample(dayOne);

        Assert.Multiple(() =>
        {
            Assert.That(firstSample, Is.Not.EqualTo(secondSample));
            Assert.That(resetSample, Is.EqualTo(firstSample));
            Assert.That(nextDaySample, Is.Not.EqualTo(firstSample));
        });
    }
}
