using NUnit.Framework;

namespace ShuffleTask.Presentation.Tests;

public class MauiProgramEventSubscriptionTests
{
    [TestCase("TaskManifestAnnouncedAsyncHandler")]
    [TestCase("TaskManifestRequestAsyncHandler")]
    [TestCase("TaskBatchResponseAsyncHandler")]
    public void InboundSyncHandler_HasSingleSubscriptionOwner(string handlerType)
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", ".."));
        var mauiProgram = File.ReadAllText(Path.Combine(repositoryRoot, "ShuffleTask.Presentation", "MauiProgram.cs"));
        var networkSyncService = File.ReadAllText(
            Path.Combine(repositoryRoot, "ShuffleTask.Application", "Services", "NetworkSyncService.cs"));

        var subscriptionMarker = $"SubscribeToEventType(new {handlerType}";
        var subscriptionCount = CountOccurrences(mauiProgram, subscriptionMarker)
            + CountOccurrences(networkSyncService, subscriptionMarker);

        Assert.That(subscriptionCount, Is.EqualTo(1),
            $"{handlerType} must be subscribed exactly once across application startup wiring.");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var startIndex = 0;

        while ((startIndex = source.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }
}
