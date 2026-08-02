using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShuffleTask.Presentation.Models;

public enum OperationStateKind
{
    Idle,
    Loading,
    Success,
    Empty,
    Validation,
    TransientFailure,
    FatalFailure
}

public sealed class OperationState : ObservableObject
{
    private Func<CancellationToken, Task>? _retry;
    private OperationStateKind _kind;
    private string _message = string.Empty;
    private string _announcement = string.Empty;
    private bool _isBlocking;
    private bool? _localDataSaved;

    public OperationState()
    {
        RetryCommand = new AsyncRelayCommand(RetryAsync, CanRetryNow);
    }

    public OperationStateKind Kind
    {
        get => _kind;
        private set => SetProperty(ref _kind, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string Announcement
    {
        get => _announcement;
        private set => SetProperty(ref _announcement, value);
    }

    public bool IsBlocking
    {
        get => _isBlocking;
        private set => SetProperty(ref _isBlocking, value);
    }

    public bool? LocalDataSaved
    {
        get => _localDataSaved;
        private set => SetProperty(ref _localDataSaved, value);
    }

    public bool IsLoading => Kind == OperationStateKind.Loading;

    public bool IsNotLoading => !IsLoading;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public bool CanRetry => _retry is not null && !IsLoading;

    public IAsyncRelayCommand RetryCommand { get; }

    public void SetIdle() => Transition(OperationStateKind.Idle, string.Empty, string.Empty, false, null, null);

    public void SetLoading(string message) => Transition(OperationStateKind.Loading, message, string.Empty, false, null, null);

    public void SetSuccess(string message, bool? localDataSaved = null)
        => Transition(OperationStateKind.Success, message, message, false, localDataSaved, null);

    public void SetEmpty(string message)
        => Transition(OperationStateKind.Empty, message, message, false, null, null);

    public void SetValidation(string message)
        => Transition(OperationStateKind.Validation, message, message, true, false, null);

    public void SetTransientFailure(
        string message,
        bool? localDataSaved,
        Func<CancellationToken, Task> retry,
        bool isBlocking = false)
    {
        ArgumentNullException.ThrowIfNull(retry);
        Transition(OperationStateKind.TransientFailure, message, message, isBlocking, localDataSaved, retry);
    }

    public void SetFatalFailure(
        string message,
        bool? localDataSaved = null,
        Func<CancellationToken, Task>? retry = null)
        => Transition(OperationStateKind.FatalFailure, message, message, true, localDataSaved, retry);

    private void Transition(
        OperationStateKind kind,
        string message,
        string announcement,
        bool isBlocking,
        bool? localDataSaved,
        Func<CancellationToken, Task>? retry)
    {
        Kind = kind;
        Message = message;
        IsBlocking = isBlocking;
        LocalDataSaved = localDataSaved;
        _retry = retry;
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsNotLoading));
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(CanRetry));
        RetryCommand.NotifyCanExecuteChanged();
        Announcement = announcement;
    }

    private bool CanRetryNow() => CanRetry;

    private Task RetryAsync(CancellationToken cancellationToken)
        => _retry?.Invoke(cancellationToken) ?? Task.CompletedTask;
}
