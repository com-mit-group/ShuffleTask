using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using ShuffleTask.Application.Models;

namespace ShuffleTask.Tests;

[TestFixture]
public class AppSettingsTests
{
    [Test]
    public void Constructor_NormalizesDefaultWeights()
    {
        var settings = new AppSettings();

        Assert.Multiple(() =>
        {
            Assert.That(settings.ImportanceWeight, Is.EqualTo(60.0));
            Assert.That(settings.UrgencyWeight, Is.EqualTo(40.0));
        });
    }

    [Test]
    public void ImportanceWeight_Setter_ClampsAndAdjustsUrgency()
    {
        var settings = new AppSettings
        {
            ImportanceWeight = 150.0
        };

        Assert.Multiple(() =>
        {
            Assert.That(settings.ImportanceWeight, Is.EqualTo(100.0));
            Assert.That(settings.UrgencyWeight, Is.EqualTo(0.0));
        });
    }

    [Test]
    public void UrgencyWeight_Setter_ClampsNegativeAndAdjustsImportance()
    {
        var settings = new AppSettings
        {
            UrgencyWeight = -25.0
        };

        Assert.Multiple(() =>
        {
            Assert.That(settings.UrgencyWeight, Is.EqualTo(0.0));
            Assert.That(settings.ImportanceWeight, Is.EqualTo(100.0));
        });
    }

    [Test]
    public void NormalizeWeights_RescalesToTargetTotal()
    {
        var settings = new AppSettings();
        SetPrivateWeights(settings, importance: 20.0, urgency: 10.0);

        settings.NormalizeWeights();

        Assert.Multiple(() =>
        {
            Assert.That(settings.ImportanceWeight, Is.EqualTo(66.6666666667).Within(1e-6));
            Assert.That(settings.UrgencyWeight, Is.EqualTo(33.3333333333).Within(1e-6));
        });
    }

    [Test]
    public void NormalizeWeights_ResetToDefaultsWhenZeroSum()
    {
        var settings = new AppSettings();
        SetPrivateWeights(settings, importance: 0.0, urgency: 0.0);

        settings.NormalizeWeights();

        Assert.Multiple(() =>
        {
            Assert.That(settings.ImportanceWeight, Is.EqualTo(60.0));
            Assert.That(settings.UrgencyWeight, Is.EqualTo(40.0));
        });
    }

    [Test]
    public void OnDeserialized_NormalizesWeightsAfterDeserialization()
    {
        var settings = new AppSettings();
        SetPrivateWeights(settings, importance: 80.0, urgency: 10.0);

        InvokeOnDeserialized(settings);

        Assert.Multiple(() =>
        {
            Assert.That(settings.ImportanceWeight, Is.EqualTo(88.8888888889).Within(1e-6));
            Assert.That(settings.UrgencyWeight, Is.EqualTo(11.1111111111).Within(1e-6));
            Assert.That(settings.ImportanceWeight + settings.UrgencyWeight, Is.EqualTo(100.0).Within(1e-6));
        });
    }

    private static void SetPrivateWeights(AppSettings settings, double importance, double urgency)
    {
        var type = typeof(AppSettings);
        type.GetField("importanceWeight", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(settings, importance);
        type.GetField("urgencyWeight", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(settings, urgency);
    }

    private static void InvokeOnDeserialized(AppSettings settings)
    {
        var method = typeof(AppSettings).GetMethod("OnDeserialized", BindingFlags.NonPublic | BindingFlags.Instance);
        method!.Invoke(settings, new object[] { default(StreamingContext) });
    }
}
