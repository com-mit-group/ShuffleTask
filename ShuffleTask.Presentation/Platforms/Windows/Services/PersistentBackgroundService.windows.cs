#if WINDOWS
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.ExtendedExecution;
using Windows.System.Threading;

namespace ShuffleTask.Presentation.Services;

internal partial class PersistentBackgroundService
{
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly object _timerLock = new();

    private ExtendedExecutionSession? _extendedSession;
    private ThreadPoolTimer? _timer;
    private CancellationTokenRegistration _timerCancellationRegistration;
    private TaskCompletionSource<bool>? _timerCompletion;
    private bool _usingExtendedExecution;

    partial void OnScheduleAsyncPartial(TimeSpan delay, CancellationToken cancellationToken, ref Task? customTask)
    {
        customTask = HandleOnScheduleAsync(delay);
    }

    partial void WaitAsyncPartial(TimeSpan delay, CancellationToken cancellationToken, ref Task? customTask)
    {
        if (!_usingExtendedExecution || delay <= TimeSpan.Zero)
        {
            return;
        }

        customTask = WaitWithExtendedExecutionAsync(delay, cancellationToken);
    }

    partial void OnCompletedAsyncPartial(bool cancelled, ref Task? customTask)
    {
        if (_usingExtendedExecution)
        {
            customTask = OnCompletedWithExtendedExecutionAsync();
        }
    }

    partial void OnCancelledPartial(ref bool handled)
    {
        if (!_usingExtendedExecution)
        {
            return;
        }

        ResolveTimerCompletion(static tcs => tcs.TrySetCanceled());
        _ = ReleaseExtendedExecutionAsync();
        _usingExtendedExecution = false;
        handled = true;
    }

    private async Task HandleOnScheduleAsync(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            _usingExtendedExecution = false;
            return;
        }

        _usingExtendedExecution = await TryEnsureExtendedExecutionAsync().ConfigureAwait(false);
        if (!_usingExtendedExecution)
        {
            Debug.WriteLine("PersistentBackgroundService: extended execution denied; falling back to in-process delay.");
        }
    }

    private async Task WaitWithExtendedExecutionAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        DateTimeOffset dueTimeUtc = DateTimeOffset.UtcNow + delay;

        lock (_timerLock)
        {
            _timerCompletion = tcs;
            _timer = ThreadPoolTimer.CreateTimer(OnTimerElapsed, delay);
            _timerCancellationRegistration = cancellationToken.Register(() => OnTimerCancelled(cancellationToken));
        }

        try
        {
            await tcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && !ex.CancellationToken.CanBeCanceled)
        {
            Debug.WriteLine("PersistentBackgroundService falling back to in-process delay after extended execution revocation.");

            TimeSpan remaining = dueTimeUtc - DateTimeOffset.UtcNow;
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task OnCompletedWithExtendedExecutionAsync()
    {
        ResolveTimerCompletion(static tcs => tcs.TrySetCanceled());
        await ReleaseExtendedExecutionAsync().ConfigureAwait(false);
        _usingExtendedExecution = false;
    }

    private async Task<bool> TryEnsureExtendedExecutionAsync()
    {
        await _sessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_extendedSession != null)
            {
                return true;
            }

            var session = new ExtendedExecutionSession
            {
                Reason = ExtendedExecutionReason.Unspecified,
                Description = "Keep ShuffleTask timers running while the app is suspended."
            };

            session.Revoked += OnExtendedExecutionRevoked;

            ExtendedExecutionResult result = await session.RequestExtensionAsync();
            if (result == ExtendedExecutionResult.Allowed)
            {
                _extendedSession = session;
                return true;
            }

            session.Revoked -= OnExtendedExecutionRevoked;
            session.Dispose();
            return false;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private async Task ReleaseExtendedExecutionAsync()
    {
        await _sessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_extendedSession != null)
            {
                _extendedSession.Revoked -= OnExtendedExecutionRevoked;
                _extendedSession.Dispose();
                _extendedSession = null;
            }
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private void OnTimerElapsed(ThreadPoolTimer timer)
    {
        ResolveTimerCompletion(static tcs => tcs.TrySetResult(true));
    }

    private void OnTimerCancelled(CancellationToken token)
    {
        ResolveTimerCompletion(tcs => tcs.TrySetCanceled(token));
    }

    private void ResolveTimerCompletion(Action<TaskCompletionSource<bool>> completionAction)
    {
        TaskCompletionSource<bool>? completion;

        lock (_timerLock)
        {
            completion = _timerCompletion;
            _timerCompletion = null;

            _timer?.Cancel();
            _timer = null;

            _timerCancellationRegistration.Dispose();
            _timerCancellationRegistration = default;
        }

        if (completion != null)
        {
            completionAction(completion);
        }
    }

    private void OnExtendedExecutionRevoked(object? sender, ExtendedExecutionRevokedEventArgs args)
    {
        Debug.WriteLine($"PersistentBackgroundService extended execution revoked: {args.Reason}");
        ResolveTimerCompletion(tcs => tcs.TrySetCanceled());
        _ = ReleaseExtendedExecutionAsync();
        _usingExtendedExecution = false;
    }
}
#endif
