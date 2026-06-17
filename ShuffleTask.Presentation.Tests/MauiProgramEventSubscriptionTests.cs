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

        var subscriptionCount = new[] { mauiProgram, networkSyncService }
            .SelectMany(source => source.Split('\n'))
            .Count(line => line.Contains("SubscribeToEventType", StringComparison.Ordinal)
                && line.Contains(handlerType, StringComparison.Ordinal));

        Assert.That(subscriptionCount, Is.EqualTo(1),
            $"{handlerType} must be subscribed exactly once across application startup wiring.");
    }
}
