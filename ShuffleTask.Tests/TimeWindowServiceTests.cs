using NUnit.Framework;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Services;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Tests;

[TestFixture]
public class TimeWindowServiceTests
{
    private static DateTimeOffset LocalDate(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local);
        return new DateTimeOffset(local);
    }

    [Test]
    public void IsWithinWorkHours_SameDayWindowHonorsBounds()
    {
        var start = new TimeSpan(9, 0, 0);
        var end = new TimeSpan(17, 0, 0);
        var inside = LocalDate(2024, 1, 2, 10, 30);
        var before = LocalDate(2024, 1, 2, 8, 59);
        var after = LocalDate(2024, 1, 2, 17, 0);

        Assert.Multiple(() =>
        {
            Assert.That(TimeWindowService.IsWithinWorkHours(inside, start, end), Is.True);
            Assert.That(TimeWindowService.IsWithinWorkHours(before, start, end), Is.False);
            Assert.That(TimeWindowService.IsWithinWorkHours(after, start, end), Is.False);
        });
    }

    [Test]
    public void IsWithinWorkHours_OvernightWindowWrapsMidnight()
    {
        var start = new TimeSpan(22, 0, 0);
        var end = new TimeSpan(6, 0, 0);
        var late = LocalDate(2024, 1, 2, 23, 0);
        var early = LocalDate(2024, 1, 3, 1, 0);
        var midday = LocalDate(2024, 1, 2, 12, 0);

        Assert.Multiple(() =>
        {
            Assert.That(TimeWindowService.IsWithinWorkHours(late, start, end), Is.True);
            Assert.That(TimeWindowService.IsWithinWorkHours(early, start, end), Is.True);
            Assert.That(TimeWindowService.IsWithinWorkHours(midday, start, end), Is.False);
        });
    }

    [Test]
    public void AllowedNow_UsesAppSettingsWindow()
    {
        var settings = new AppSettings
        {
            WorkStart = new TimeSpan(9, 0, 0),
            WorkEnd = new TimeSpan(17, 0, 0)
        };
        var now = LocalDate(2024, 1, 2, 11, 0);

        Assert.Multiple(() =>
        {
            Assert.That(TimeWindowService.AllowedNow(AllowedPeriod.Any, now, settings), Is.True);
            Assert.That(TimeWindowService.AllowedNow(AllowedPeriod.Work, now, settings), Is.True);
            Assert.That(TimeWindowService.AllowedNow(AllowedPeriod.OffWork, now, settings), Is.False);
        });
    }

    [Test]
    public void UntilNextBoundary_ReturnsZeroWhenAlwaysAllowed()
    {
        var settings = new AppSettings
        {
            WorkStart = new TimeSpan(9, 0, 0),
            WorkEnd = new TimeSpan(9, 0, 0)
        };
        var now = LocalDate(2024, 1, 2, 11, 0);

        Assert.That(TimeWindowService.UntilNextBoundary(now, settings), Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void UntilNextBoundary_ComputesTimeUntilWindowEdge()
    {
        var settings = new AppSettings
        {
            WorkStart = new TimeSpan(9, 0, 0),
            WorkEnd = new TimeSpan(17, 0, 0)
        };
        var inside = LocalDate(2024, 1, 2, 12, 0);
        var outside = LocalDate(2024, 1, 2, 7, 30);

        var withinResult = TimeWindowService.UntilNextBoundary(inside, settings);
        var outsideResult = TimeWindowService.UntilNextBoundary(outside, settings);

        Assert.Multiple(() =>
        {
            Assert.That(withinResult, Is.EqualTo(TimeSpan.FromHours(5)));
            Assert.That(outsideResult, Is.EqualTo(TimeSpan.FromHours(1.5)));
        });
    }
}
