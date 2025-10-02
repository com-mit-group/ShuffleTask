using System;
using NUnit.Framework;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Services;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Tests;

[TestFixture]
public class ManualShuffleServiceTests
{
    [Test]
    public void CreateCandidatePool_RespectsAllowedPeriodSetting()
    {
        var original = new TaskItem
        {
            Id = "respect",
            AllowedPeriod = AllowedPeriod.Work,
            AutoShuffleAllowed = false
        };

        var settings = new AppSettings
        {
            ManualShuffleRespectsAllowedPeriod = true
        };

        var pool = ManualShuffleService.CreateCandidatePool(new[] { original }, settings);

        Assert.That(pool, Has.Count.EqualTo(1), "Expected a single candidate.");
        var candidate = pool[0];
        Assert.AreNotSame(original, candidate, "Manual shuffle pool should clone tasks.");
        Assert.That(candidate.AllowedPeriod, Is.EqualTo(AllowedPeriod.Work), "Allowed period should be preserved when respecting settings.");
        Assert.That(candidate.AutoShuffleAllowed, Is.True, "Manual shuffle should bypass AutoShuffleAllowed flag.");
        Assert.That(original.AutoShuffleAllowed, Is.False, "Original task should remain unchanged.");
    }

    [Test]
    public void CreateCandidatePool_IgnoresAllowedPeriodWhenDisabled()
    {
        var original = new TaskItem
        {
            Id = "ignore",
            AllowedPeriod = AllowedPeriod.Custom,
            AutoShuffleAllowed = false,
            CustomStartTime = TimeSpan.FromHours(8),
            CustomEndTime = TimeSpan.FromHours(12)
        };

        var settings = new AppSettings
        {
            ManualShuffleRespectsAllowedPeriod = false
        };

        var pool = ManualShuffleService.CreateCandidatePool(new[] { original }, settings);

        Assert.That(pool, Has.Count.EqualTo(1), "Expected a single candidate.");
        var candidate = pool[0];
        Assert.That(candidate.AllowedPeriod, Is.EqualTo(AllowedPeriod.Any), "Allowed period should be cleared when ignoring time windows.");
        Assert.IsNull(candidate.CustomStartTime, "Custom start time should be cleared when ignoring the window.");
        Assert.IsNull(candidate.CustomEndTime, "Custom end time should be cleared when ignoring the window.");
        Assert.That(candidate.AutoShuffleAllowed, Is.True, "Manual shuffle should bypass AutoShuffleAllowed flag.");

        Assert.That(original.AllowedPeriod, Is.EqualTo(AllowedPeriod.Custom), "Original task should not be mutated.");
        Assert.IsNotNull(original.CustomStartTime, "Original custom start time should remain.");
        Assert.IsNotNull(original.CustomEndTime, "Original custom end time should remain.");
    }
}
