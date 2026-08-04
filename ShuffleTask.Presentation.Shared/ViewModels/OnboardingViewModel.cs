using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Presentation.Models;

namespace ShuffleTask.ViewModels;

public enum OnboardingOutcome
{
    CreatedTask,
    AddedSamples,
    ContinuedEmpty
}

public sealed class OnboardingCompletedEventArgs(OnboardingOutcome outcome) : EventArgs
{
    public OnboardingOutcome Outcome { get; } = outcome;
}

public partial class OnboardingViewModel : ObservableObject
{
    public const int CurrentVersion = 1;

    private readonly IOnboardingService _onboarding;
    private readonly TimeProvider _clock;
    private int _operationRunning;
    private bool _choicesReady;

    public OnboardingViewModel(IOnboardingService onboarding, TimeProvider clock)
    {
        _onboarding = onboarding ?? throw new ArgumentNullException(nameof(onboarding));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public event EventHandler? CreateTaskRequested;
    public event EventHandler<OnboardingCompletedEventArgs>? Completed;

    public OperationState OperationState { get; } = new();

    [ObservableProperty]
    private bool isVisible = true;

    public bool CanChoose => _choicesReady
        && IsVisible
        && Volatile.Read(ref _operationRunning) == 0
        && !OperationState.IsLoading;

    public void SetStartupLoading()
    {
        _choicesReady = false;
        IsVisible = true;
        OperationState.SetLoading("Preparing your workspace…");
        RefreshCommands();
    }

    public async Task<bool> LoadAsync(CancellationToken cancellationToken = default)
    {
        OperationState.SetLoading("Checking first-run setup…");
        RefreshCommands();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            int completedVersion = await _onboarding.GetCompletedVersionAsync();
            if (completedVersion >= CurrentVersion)
            {
                _choicesReady = false;
                IsVisible = false;
                OperationState.SetIdle();
                return false;
            }

            _choicesReady = true;
            IsVisible = true;
            OperationState.SetSuccess("Choose how you want to start ShuffleTask.");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _choicesReady = false;
            IsVisible = true;
            OperationState.SetTransientFailure(
                "Setup was canceled. Retry to continue.",
                false,
                LoadAsync,
                isBlocking: true);
            return true;
        }
        catch
        {
            _choicesReady = false;
            IsVisible = true;
            OperationState.SetTransientFailure(
                "ShuffleTask could not read first-run setup. Your existing data is unchanged.",
                false,
                LoadAsync,
                isBlocking: true);
            return true;
        }
        finally
        {
            RefreshCommands();
        }
    }

    public void SetStartupFailure(Func<CancellationToken, Task> retry)
    {
        ArgumentNullException.ThrowIfNull(retry);
        _choicesReady = false;
        IsVisible = true;
        OperationState.SetTransientFailure(
            "ShuffleTask could not initialize local storage safely. Check storage access and retry.",
            false,
            retry,
            isBlocking: true);
        RefreshCommands();
    }

    public void ReturnToChoices()
    {
        _choicesReady = true;
        IsVisible = true;
        OperationState.SetSuccess("No task was created. Choose how you want to continue.");
        RefreshCommands();
    }

    [RelayCommand(CanExecute = nameof(CanChoose))]
    private void CreateTask()
    {
        if (!CanChoose)
        {
            return;
        }

        _choicesReady = false;
        OperationState.SetLoading("Opening the new task editor…");
        RefreshCommands();
        CreateTaskRequested?.Invoke(this, EventArgs.Empty);
    }

    public Task CompleteCreatedTaskAsync(CancellationToken cancellationToken = default)
        => RunCompletionAsync(
            OnboardingOutcome.CreatedTask,
            "Saving first-run choice…",
            "Your first task is ready.",
            () => _onboarding.CompleteAsync(CurrentVersion),
            cancellationToken);

    [RelayCommand(CanExecute = nameof(CanChoose))]
    private Task AddSampleTasksAsync(CancellationToken cancellationToken)
        => RunCompletionAsync(
            OnboardingOutcome.AddedSamples,
            "Adding sample tasks…",
            "Sample tasks added. You can edit or delete them at any time.",
            () => _onboarding.CompleteWithSamplesAsync(CreateSamples(), CurrentVersion),
            cancellationToken);

    [RelayCommand(CanExecute = nameof(CanChoose))]
    private Task ContinueWithoutSamplesAsync(CancellationToken cancellationToken)
        => RunCompletionAsync(
            OnboardingOutcome.ContinuedEmpty,
            "Saving your choice…",
            "Starting with an empty task list.",
            () => _onboarding.CompleteAsync(CurrentVersion),
            cancellationToken);

    private async Task RunCompletionAsync(
        OnboardingOutcome outcome,
        string loadingMessage,
        string successMessage,
        Func<Task> persist,
        CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _operationRunning, 1, 0) != 0)
        {
            return;
        }

        _choicesReady = false;
        OperationState.SetLoading(loadingMessage);
        RefreshCommands();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await persist();
            IsVisible = false;
            OperationState.SetSuccess(successMessage, localDataSaved: true);
            Completed?.Invoke(this, new OnboardingCompletedEventArgs(outcome));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            IsVisible = true;
            OperationState.SetTransientFailure(
                "Setup was canceled before your choice was saved. Retry to continue.",
                false,
                token => RunCompletionAsync(outcome, loadingMessage, successMessage, persist, token),
                isBlocking: true);
        }
        catch
        {
            IsVisible = true;
            OperationState.SetTransientFailure(
                "Your choice could not be saved. No sample tasks or setup marker were committed. Retry to continue.",
                false,
                token => RunCompletionAsync(outcome, loadingMessage, successMessage, persist, token),
                isBlocking: true);
        }
        finally
        {
            Interlocked.Exchange(ref _operationRunning, 0);
            RefreshCommands();
        }
    }

    private IReadOnlyCollection<TaskItem> CreateSamples()
    {
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
        return new[]
        {
            new TaskItem { Id = "onboarding-v1-dishes", Title = "Dishes", Importance = 3, Repeat = RepeatType.Daily, AllowedPeriod = AllowedPeriod.OffWork },
            new TaskItem { Id = "onboarding-v1-inbox-zero", Title = "Inbox Zero", Importance = 4, Repeat = RepeatType.Interval, IntervalDays = 2, AllowedPeriod = AllowedPeriod.Work },
            new TaskItem { Id = "onboarding-v1-laundry", Title = "Laundry", Importance = 2, Repeat = RepeatType.Weekly, Weekdays = Weekdays.Sat, AllowedPeriod = AllowedPeriod.OffWork },
            new TaskItem { Id = "onboarding-v1-tax-paperwork", Title = "Tax paperwork", Importance = 5, Repeat = RepeatType.None, Deadline = nowUtc.AddDays(3), AllowedPeriod = AllowedPeriod.Any }
        };
    }

    partial void OnIsVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(CanChoose));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanChoose));
        CreateTaskCommand.NotifyCanExecuteChanged();
        AddSampleTasksCommand.NotifyCanExecuteChanged();
        ContinueWithoutSamplesCommand.NotifyCanExecuteChanged();
    }
}
