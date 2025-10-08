using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ShuffleTask.Presentation.Services;

public interface IPersistentBackgroundService
{
    Task ScheduleAsync(TimeSpan delay, CancellationToken cancellationToken, Func<Task> callback);

    void Cancel();
}

internal partial class PersistentBackgroundService : IPersistentBackgroundService, IDisposable
{
    public Task ScheduleAsync(TimeSpan delay, CancellationToken cancellationToken, Func<Task> callback)
    {
        if (callback == null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        return RunTimerAsync(delay, cancellationToken, callback);
    }

    private async Task RunTimerAsync(TimeSpan delay, CancellationToken cancellationToken, Func<Task> callback)
    {
        try
        {
            await OnScheduleAsync(delay, cancellationToken).ConfigureAwait(false);
            await WaitAsync(delay, cancellationToken).ConfigureAwait(false);

            if (!cancellationToken.IsCancellationRequested)
            {
                await callback().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Swallow expected cancellation
        }
        catch (Exception ex)
        {
            OnUnhandledException(ex);
        }
        finally
        {
            await OnCompletedAsync(cancellationToken.IsCancellationRequested).ConfigureAwait(false);
        }
    }

    public void Cancel()
    {
        OnCancelled();
    }

    protected virtual Task OnScheduleAsync(TimeSpan delay, CancellationToken cancellationToken)
        => Task.CompletedTask;

    protected virtual Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        => Task.Delay(delay, cancellationToken);

    protected virtual Task OnCompletedAsync(bool cancelled)
        => Task.CompletedTask;

    protected virtual void OnCancelled()
    {
    }

    protected virtual void OnUnhandledException(Exception exception)
    {
        Debug.WriteLine($"PersistentBackgroundService error: {exception}");
    }

    public void Dispose()
    {
        Cancel();
    }
}
