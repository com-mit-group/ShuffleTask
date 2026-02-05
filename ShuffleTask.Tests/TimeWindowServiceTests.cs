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
    public void AllowedNow_WeekendOverridesWorkAndCustom()
    {
        var settings = new AppSettings
        {
            WorkStart = new TimeSpan(9, 0, 0),
            WorkEnd = new TimeSpan(17, 0, 0)
        };
        var weekend = LocalDate(2024, 1, 6, 11, 0);

        Assert.Multiple(() =>
        {
            Assert.That(TimeWindowService.AllowedNow(AllowedPeriod.Work, weekend, settings), Is.False);
            Assert.That(TimeWindowService.AllowedNow(AllowedPeriod.OffWork, weekend, settings), Is.True);
        });
    }

    [Test]
    public void AllowedNow_AllDayDefinitionHonorsWeekdayConstraints()
    {
        var settings = new AppSettings();
        var monday = LocalDate(2024, 1, 1, 9, 0);
        var tuesday = LocalDate(2024, 1, 2, 9, 0);

        var definition = new PeriodDefinition
        {
            Id = "weekday-only",
            Name = "Weekday only",
            Weekdays = Weekdays.Mon | Weekdays.Wed,
            IsAllDay = true,
            Mode = PeriodDefinitionMode.None
        };

        Assert.Multiple(() =>
        {
            Assert.That(TimeWindowService.AllowedNow(definition, monday, settings), Is.True);
            Assert.That(TimeWindowService.AllowedNow(definition, tuesday, settings), Is.False);
        });
    }

    [Test]
    public void AllowedNow_WorkAndOffWorkAlignWithAppSettings()
    {
        var settings = new AppSettings
        {
            WorkStart = new TimeSpan(6, 0, 0),
            WorkEnd = new TimeSpan(14, 0, 0)
        };
        var weekdayEarly = LocalDate(2024, 1, 2, 5, 30);
        var weekdayInside = LocalDate(2024, 1, 2, 7, 0);
        var weekdayOutside = LocalDate(2024, 1, 2, 15, 0);
        var weekendDuringWork = LocalDate(2024, 1, 6, 7, 0);

        Assert.Multiple(() =>
        {
            Assert.That(TimeWindowService.AllowedNow(PeriodDefinitionCatalog.Work, weekdayInside, settings), Is.True);
            Assert.That(TimeWindowService.AllowedNow(PeriodDefinitionCatalog.Work, weekdayOutside, settings), Is.False);
            Assert.That(TimeWindowService.AllowedNow(PeriodDefinitionCatalog.OffWork, weekdayEarly, settings), Is.True);
            Assert.That(TimeWindowService.AllowedNow(PeriodDefinitionCatalog.OffWork, weekdayInside, settings), Is.False);
            Assert.That(TimeWindowService.AllowedNow(PeriodDefinitionCatalog.OffWork, weekendDuringWork, settings), Is.True);
        });
    }

    [Test]
    public void AllowedNow_DefaultRangesForMorningsEveningsAndLunch()
    {
        var settings = new AppSettings
        {
            WorkStart = new TimeSpan(9, 0, 0),
            WorkEnd = new TimeSpan(17, 0, 0),
            MorningStart = new TimeSpan(6, 0, 0),
            MorningEnd = new TimeSpan(9, 0, 0),
            LunchStart = new TimeSpan(11, 30, 0),
            LunchEnd = new TimeSpan(12, 30, 0),
            EveningStart = new TimeSpan(19, 0, 0),
            EveningEnd = new TimeSpan(22, 0, 0)
        };
        var morningInside = LocalDate(2024, 1, 2, 7, 30);
        var morningOutside = LocalDate(2024, 1, 2, 10, 0);
        var eveningInside = LocalDate(2024, 1, 2, 21, 0);
        var eveningOutside = LocalDate(2024, 1, 2, 10, 0);
        var lunchInside = LocalDate(2024, 1, 2, 12, 0);
        var lunchOutside = LocalDate(2024, 1, 2, 14, 0);

        Assert.Multiple(() =>
        {
            Assert.That(TimeWindowService.AllowedNow(PeriodDefinitionCatalog.Mornings, morningInside, settings), Is.True);
            Assert.That(TimeWindowService.AllowedNow(PeriodDefinitionCatalog.Mornings, morningOutside, settings), Is.False);
            Assert.That(TimeWindowService.AllowedNow(PeriodDefinitionCatalog.Evenings, eveningInside, settings), Is.True);
            Assert.That(TimeWindowService.AllowedNow(PeriodDefinitionCatalog.Evenings, eveningOutside, settings), Is.False);
            Assert.That(TimeWindowService.AllowedNow(PeriodDefinitionCatalog.LunchBreak, lunchInside, settings), Is.True);
            Assert.That(TimeWindowService.AllowedNow(PeriodDefinitionCatalog.LunchBreak, lunchOutside, settings), Is.False);
        });
    }

    [Test]
    public void AllowedNow_MorningMode_UsesSettingsTimesAndWeekdays()
    {
        var settings = new AppSettings
        {
            MorningStart = new TimeSpan(5, 0, 0),
            MorningEnd = new TimeSpan(8, 0, 0)
        };
        var mondayMorning = LocalDate(2024, 1, 1, 6, 30);
        var tuesdayMorning = LocalDate(2024, 1, 2, 6, 30);
        var mondayLate = LocalDate(2024, 1, 1, 9, 0);

        var definition = new PeriodDefinition
        {
            Id = "custom-morning",
            Name = "Custom morning",
            Weekdays = Weekdays.Mon,
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(10, 0, 0),
            IsAllDay = false,
            Mode = PeriodDefinitionMode.Morning
        };

        Assert.Multiple(() =>
        {
            Assert.That(TimeWindowService.AllowedNow(definition, mondayMorning, settings), Is.True);
            Assert.That(TimeWindowService.AllowedNow(definition, tuesdayMorning, settings), Is.False);
            Assert.That(TimeWindowService.AllowedNow(definition, mondayLate, settings), Is.False);
        });
    }

    [Test]
    public void AllowedNow_LunchMode_WeekendOnlyRespectsSettingsWindow()
    {
        var settings = new AppSettings
        {
            LunchStart = new TimeSpan(11, 0, 0),
            LunchEnd = new TimeSpan(12, 0, 0)
        };
        var saturdayLunch = LocalDate(2024, 1, 6, 11, 30);
        var saturdayLate = LocalDate(2024, 1, 6, 13, 0);
        var mondayLunch = LocalDate(2024, 1, 1, 11, 30);

        var definition = new PeriodDefinition
        {
            Id = "weekend-lunch",
            Name = "Weekend lunch",
            Weekdays = Weekdays.Sat | Weekdays.Sun,
            StartTime = new TimeSpan(13, 0, 0),
            EndTime = new TimeSpan(14, 0, 0),
            IsAllDay = false,
            Mode = PeriodDefinitionMode.Lunch
        };

        Assert.Multiple(() =>
        {
            Assert.That(TimeWindowService.AllowedNow(definition, saturdayLunch, settings), Is.True);
            Assert.That(TimeWindowService.AllowedNow(definition, saturdayLate, settings), Is.False);
            Assert.That(TimeWindowService.AllowedNow(definition, mondayLunch, settings), Is.False);
        });
    }

    [Test]
    public void AllowedNow_EveningMode_UsesSettingsTimesAndWeekdays()
    {
        var settings = new AppSettings
        {
            EveningStart = new TimeSpan(20, 0, 0),
            EveningEnd = new TimeSpan(22, 0, 0)
        };
        var tuesdayEvening = LocalDate(2024, 1, 2, 21, 0);
        var wednesdayEvening = LocalDate(2024, 1, 3, 21, 0);
        var tuesdayLate = LocalDate(2024, 1, 2, 23, 0);

        var definition = new PeriodDefinition
        {
            Id = "weekday-evening",
            Name = "Weekday evening",
            Weekdays = Weekdays.Tue,
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(19, 0, 0),
            IsAllDay = false,
            Mode = PeriodDefinitionMode.Evening
        };

        Assert.Multiple(() =>
        {
            Assert.That(TimeWindowService.AllowedNow(definition, tuesdayEvening, settings), Is.True);
            Assert.That(TimeWindowService.AllowedNow(definition, wednesdayEvening, settings), Is.False);
            Assert.That(TimeWindowService.AllowedNow(definition, tuesdayLate, settings), Is.False);
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

    [Test]
    public void AutoShuffleAllowedNow_RespectsFlagWhenFalse()
    {
        var settings = new AppSettings
        {
            WorkStart = new TimeSpan(9, 0, 0),
            WorkEnd = new TimeSpan(17, 0, 0)
        };
        var now = LocalDate(2024, 1, 2, 11, 0);

        var task = new TaskItem
        {
            AllowedPeriod = AllowedPeriod.Any,
            AutoShuffleAllowed = false
        };

        Assert.That(TimeWindowService.AutoShuffleAllowedNow(task, now, settings), Is.False,
            "Task with AutoShuffleAllowed=false should not be eligible for auto-shuffle");
    }

    [Test]
    public void AutoShuffleAllowedNow_WorksWithCustomTimeWindow()
    {
        var settings = new AppSettings
        {
            WorkStart = new TimeSpan(9, 0, 0),
            WorkEnd = new TimeSpan(17, 0, 0)
        };
        var inside = LocalDate(2024, 1, 2, 11, 0);
        var outside = LocalDate(2024, 1, 2, 19, 0);

        var task = new TaskItem
        {
            AllowedPeriod = AllowedPeriod.Custom,
            AutoShuffleAllowed = true,
            CustomStartTime = new TimeSpan(10, 0, 0),
            CustomEndTime = new TimeSpan(14, 0, 0)
        };

        Assert.Multiple(() =>
        {
            Assert.That(TimeWindowService.AutoShuffleAllowedNow(task, inside, settings), Is.True,
                "Task should be allowed during custom time window");
            Assert.That(TimeWindowService.AutoShuffleAllowedNow(task, outside, settings), Is.False,
                "Task should not be allowed outside custom time window");
        });
    }

    [Test]
    public void AutoShuffleAllowedNow_CustomWeekdays_RestrictsWeekend()
    {
        var settings = new AppSettings
        {
            WorkStart = new TimeSpan(9, 0, 0),
            WorkEnd = new TimeSpan(17, 0, 0)
        };
        var weekday = LocalDate(2024, 1, 2, 11, 0);
        var weekend = LocalDate(2024, 1, 6, 11, 0);

        var task = new TaskItem
        {
            AllowedPeriod = AllowedPeriod.Custom,
            AutoShuffleAllowed = true,
            CustomStartTime = new TimeSpan(10, 0, 0),
            CustomEndTime = new TimeSpan(14, 0, 0),
            CustomWeekdays = Weekdays.Mon | Weekdays.Tue
        };

        Assert.Multiple(() =>
        {
            Assert.That(TimeWindowService.AutoShuffleAllowedNow(task, weekday, settings), Is.True,
                "Task should be allowed on configured weekdays within the time range");
            Assert.That(TimeWindowService.AutoShuffleAllowedNow(task, weekend, settings), Is.False,
                "Task should not be allowed on weekends when custom weekdays exclude them");
        });
    }

    [Test]
    public void AutoShuffleAllowedNow_WeekendOverridesWorkAndCustom()
    {
        var settings = new AppSettings
        {
            WorkStart = new TimeSpan(9, 0, 0),
            WorkEnd = new TimeSpan(17, 0, 0)
        };
        var weekend = LocalDate(2024, 1, 6, 11, 0);

        var workTask = new TaskItem
        {
            AllowedPeriod = AllowedPeriod.Work,
            AutoShuffleAllowed = true
        };

        var offWorkTask = new TaskItem
        {
            AllowedPeriod = AllowedPeriod.OffWork,
            AutoShuffleAllowed = true
        };

        var customTask = new TaskItem
        {
            AllowedPeriod = AllowedPeriod.Custom,
            AutoShuffleAllowed = true,
            CustomStartTime = new TimeSpan(10, 0, 0),
            CustomEndTime = new TimeSpan(14, 0, 0)
        };

        Assert.Multiple(() =>
        {
            Assert.That(TimeWindowService.AutoShuffleAllowedNow(workTask, weekend, settings), Is.False);
            Assert.That(TimeWindowService.AutoShuffleAllowedNow(offWorkTask, weekend, settings), Is.True);
            Assert.That(TimeWindowService.AutoShuffleAllowedNow(customTask, weekend, settings), Is.True);
        });
    }

    [Test]
    public void AutoShuffleAllowedNow_CustomWithNullTimesDefaultsToAllowed()
    {
        var settings = new AppSettings();
        var now = LocalDate(2024, 1, 2, 11, 0);

        var task = new TaskItem
        {
            AllowedPeriod = AllowedPeriod.Custom,
            AutoShuffleAllowed = true,
            CustomStartTime = null,
            CustomEndTime = null
        };

        Assert.That(TimeWindowService.AutoShuffleAllowedNow(task, now, settings), Is.True,
            "Task with Custom period but null times should default to allowed");
    }
}
