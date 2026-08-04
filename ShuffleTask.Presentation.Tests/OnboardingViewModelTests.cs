using NSubstitute;
using NUnit.Framework;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Presentation.Models;
using ShuffleTask.ViewModels;

namespace ShuffleTask.Presentation.Tests;

[TestFixture]
public class OnboardingViewModelTests
{
    [Test]
    public async Task LoadAsync_IncompleteSetup_ShowsChoices()
    {
        var onboarding = Substitute.For<IOnboardingService>();
        onboarding.GetCompletedVersionAsync().Returns(0);
        var viewModel = new OnboardingViewModel(onboarding, TimeProvider.System);

        bool visible = await viewModel.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(visible, Is.True);
            Assert.That(viewModel.IsVisible, Is.True);
            Assert.That(viewModel.CanChoose, Is.True);
            Assert.That(viewModel.OperationState.Kind, Is.EqualTo(OperationStateKind.Success));
        });
    }

    [Test]
    public async Task LoadAsync_CompletedSetup_DoesNotShowOnboarding()
    {
        var onboarding = Substitute.For<IOnboardingService>();
        onboarding.GetCompletedVersionAsync().Returns(OnboardingViewModel.CurrentVersion);
        var viewModel = new OnboardingViewModel(onboarding, TimeProvider.System);

        bool visible = await viewModel.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(visible, Is.False);
            Assert.That(viewModel.IsVisible, Is.False);
            Assert.That(viewModel.OperationState.Kind, Is.EqualTo(OperationStateKind.Idle));
        });
    }

    [Test]
    public async Task ContinueWithoutSamples_PersistsBeforeCompletion()
    {
        var onboarding = Substitute.For<IOnboardingService>();
        onboarding.CompleteAsync(Arg.Any<int>()).Returns(Task.CompletedTask);
        var viewModel = new OnboardingViewModel(onboarding, TimeProvider.System);
        OnboardingOutcome? outcome = null;
        viewModel.Completed += (_, args) => outcome = args.Outcome;

        await viewModel.ContinueWithoutSamplesCommand.ExecuteAsync(null);

        await onboarding.Received(1).CompleteAsync(OnboardingViewModel.CurrentVersion);
        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(OnboardingOutcome.ContinuedEmpty));
            Assert.That(viewModel.IsVisible, Is.False);
            Assert.That(viewModel.OperationState.LocalDataSaved, Is.True);
        });
    }

    [Test]
    public async Task AddSamples_FailureRetainsIntentAndRetryCompletesOnce()
    {
        var onboarding = Substitute.For<IOnboardingService>();
        onboarding.CompleteWithSamplesAsync(Arg.Any<IReadOnlyCollection<TaskItem>>(), Arg.Any<int>())
            .Returns(
                Task.FromException(new IOException("Injected failure.")),
                Task.CompletedTask);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero));
        var viewModel = new OnboardingViewModel(onboarding, clock);
        int completionCount = 0;
        viewModel.Completed += (_, args) =>
        {
            if (args.Outcome == OnboardingOutcome.AddedSamples)
            {
                completionCount++;
            }
        };

        await viewModel.AddSampleTasksCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.OperationState.Kind, Is.EqualTo(OperationStateKind.TransientFailure));
            Assert.That(viewModel.OperationState.CanRetry, Is.True);
            Assert.That(viewModel.IsVisible, Is.True);
            Assert.That(viewModel.CanChoose, Is.False);
            Assert.That(completionCount, Is.Zero);
        });

        await viewModel.OperationState.RetryCommand.ExecuteAsync(null);

        await onboarding.Received(2).CompleteWithSamplesAsync(
            Arg.Is<IReadOnlyCollection<TaskItem>>(samples =>
                samples.Count == 4
                && samples.Select(sample => sample.Id).Distinct(StringComparer.Ordinal).Count() == 4),
            OnboardingViewModel.CurrentVersion);
        Assert.Multiple(() =>
        {
            Assert.That(completionCount, Is.EqualTo(1));
            Assert.That(viewModel.IsVisible, Is.False);
            Assert.That(viewModel.OperationState.Kind, Is.EqualTo(OperationStateKind.Success));
        });
    }

    [Test]
    public async Task CreateTask_CompletesOnlyAfterSavedTaskCallback()
    {
        var onboarding = Substitute.For<IOnboardingService>();
        onboarding.CompleteAsync(Arg.Any<int>()).Returns(Task.CompletedTask);
        var viewModel = new OnboardingViewModel(onboarding, TimeProvider.System);
        int requests = 0;
        viewModel.CreateTaskRequested += (_, _) => requests++;

        viewModel.CreateTaskCommand.Execute(null);

        Assert.That(requests, Is.EqualTo(1));
        await onboarding.DidNotReceive().CompleteAsync(Arg.Any<int>());

        viewModel.ReturnToChoices();
        await viewModel.CompleteCreatedTaskAsync();

        await onboarding.Received(1).CompleteAsync(OnboardingViewModel.CurrentVersion);
    }

    [Test]
    public void StartupFailure_DisablesChoicesAndLeavesSingleRetry()
    {
        var onboarding = Substitute.For<IOnboardingService>();
        var viewModel = new OnboardingViewModel(onboarding, TimeProvider.System);

        viewModel.SetStartupFailure(_ => Task.CompletedTask);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanChoose, Is.False);
            Assert.That(viewModel.OperationState.CanRetry, Is.True);
            Assert.That(viewModel.OperationState.Kind, Is.EqualTo(OperationStateKind.TransientFailure));
        });
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
