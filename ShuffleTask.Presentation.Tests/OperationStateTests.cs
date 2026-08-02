using NUnit.Framework;
using ShuffleTask.Presentation.Models;

namespace ShuffleTask.Presentation.Tests;

[TestFixture]
public class OperationStateTests
{
    [Test]
    public void Transitions_RepresentEveryRequiredStateAndCommitResult()
    {
        var state = new OperationState();

        Assert.That(state.Kind, Is.EqualTo(OperationStateKind.Idle));

        state.SetLoading("Loading");
        Assert.Multiple(() =>
        {
            Assert.That(state.Kind, Is.EqualTo(OperationStateKind.Loading));
            Assert.That(state.IsLoading, Is.True);
            Assert.That(state.IsNotLoading, Is.False);
        });

        state.SetSuccess("Saved", true);
        Assert.Multiple(() =>
        {
            Assert.That(state.Kind, Is.EqualTo(OperationStateKind.Success));
            Assert.That(state.LocalDataSaved, Is.True);
            Assert.That(state.Announcement, Is.EqualTo("Saved"));
        });

        state.SetEmpty("Nothing here");
        Assert.That(state.Kind, Is.EqualTo(OperationStateKind.Empty));

        state.SetValidation("Title required");
        Assert.Multiple(() =>
        {
            Assert.That(state.Kind, Is.EqualTo(OperationStateKind.Validation));
            Assert.That(state.IsBlocking, Is.True);
            Assert.That(state.LocalDataSaved, Is.False);
        });

        state.SetTransientFailure("Try again", false, _ => Task.CompletedTask);
        Assert.Multiple(() =>
        {
            Assert.That(state.Kind, Is.EqualTo(OperationStateKind.TransientFailure));
            Assert.That(state.CanRetry, Is.True);
            Assert.That(state.LocalDataSaved, Is.False);
        });

        state.SetFatalFailure("Cannot continue");
        Assert.Multiple(() =>
        {
            Assert.That(state.Kind, Is.EqualTo(OperationStateKind.FatalFailure));
            Assert.That(state.IsBlocking, Is.True);
            Assert.That(state.CanRetry, Is.False);
        });
    }

    [Test]
    public async Task RetryCommand_InvokesRetryAndPreventsConcurrentExecution()
    {
        var state = new OperationState();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;

        state.SetTransientFailure("Try again", false, async cancellationToken =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        });

        Task first = state.RetryCommand.ExecuteAsync(null);
        await started.Task;
        Task second = state.RetryCommand.ExecuteAsync(null);

        Assert.That(calls, Is.EqualTo(1));

        release.SetResult();
        await Task.WhenAll(first, second);
    }
}
