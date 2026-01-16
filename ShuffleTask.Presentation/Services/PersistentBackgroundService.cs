using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ShuffleTask.Presentation.Services;

internal partial class PersistentBackgroundService : IPersistentBackgroundService, IDisposable
{
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly IPersistentBackgroundPlatform _platform;
    private bool _initialized;
    private bool _disposed;

    public PersistentBackgroundService(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        IPersistentBackgroundPlatform? platform = null;
        InitializePlatform(_clock, ref platform);
        _platform = platform ?? new NoOpPersistentBackgroundPlatform();
    }

    public async Task InitializeAsync()
    {
        ThrowIfDisposed();

        if (_initialized)
        {
            return;
        }

        await _initializationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await _platform.InitializeAsync().ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public Task ScheduleAsync(TimeSpan delay, CancellationToken cancellationToken, Func<Task> callback)
    {
        ThrowIfDisposed();

        if (callback is null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        return RunTimerAsync(delay, cancellationToken, callback);
    }

    public void Schedule(DateTimeOffset when, string? taskId)
    {
        ThrowIfDisposed();

        TimeSpan delay = when - _clock.GetUtcNow();
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        _platform.Schedule(when, delay, taskId);
    }

    public void Cancel()
    {
        if (_disposed)
        {
            return;
        }

        _platform.Cancel();
        OnCancelledCore();
    }

    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        _platform.Stop();
    }

    private async Task RunTimerAsync(TimeSpan delay, CancellationToken cancellationToken, Func<Task> callback)
    {
        try
        {
            await OnScheduleAsyncCore(delay, cancellationToken).ConfigureAwait(false);
            await WaitAsyncCore(delay, cancellationToken).ConfigureAwait(false);

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
            await OnCompletedAsyncCore(cancellationToken.IsCancellationRequested).ConfigureAwait(false);
        }
    }

    private Task OnScheduleAsyncCore(TimeSpan delay, CancellationToken cancellationToken)
    {
        Task? customTask = null;
        OnScheduleAsyncPartial(delay, cancellationToken, ref customTask);
        return customTask ?? Task.CompletedTask;
    }

    private Task WaitAsyncCore(TimeSpan delay, CancellationToken cancellationToken)
    {
        Task? customTask = null;
        WaitAsyncPartial(delay, cancellationToken, ref customTask);
        return customTask ?? Task.Delay(delay, cancellationToken);
    }

    private Task OnCompletedAsyncCore(bool cancelled)
    {
        Task? customTask = null;
        OnCompletedAsyncPartial(cancelled, ref customTask);
        return customTask ?? Task.CompletedTask;
    }

    private void OnCancelledCore()
    {
        bool handled = false;
        OnCancelledPartial(ref handled);

        if (!handled)
        {
            // The default implementation performs no additional work when cancellation occurs.
        }
    }

    protected virtual void OnUnhandledException(Exception exception)
    {
        Debug.WriteLine($"PersistentBackgroundService error: {exception}");
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            try
            {
                Cancel();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PersistentBackgroundService dispose cancel error: {ex}");
            }

            _initializationGate.Dispose();
        }

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PersistentBackgroundService));
        }
    }

    private interface IPersistentBackgroundPlatform
    {
        Task InitializeAsync();

        void Schedule(DateTimeOffset when, TimeSpan delay, string? taskId);

        void Cancel();

        void Stop();
    }

    private sealed class NoOpPersistentBackgroundPlatform : IPersistentBackgroundPlatform
    {
        public Task InitializeAsync() => Task.CompletedTask;

        public void Schedule(DateTimeOffset when, TimeSpan delay, string? taskId)
        {
        }

        public void Cancel()
        {
        }

        public void Stop()
        {
        }
    }

    partial void InitializePlatform(TimeProvider clock, ref IPersistentBackgroundPlatform? platform);

    partial void OnScheduleAsyncPartial(TimeSpan delay, CancellationToken cancellationToken, ref Task? customTask);

    partial void WaitAsyncPartial(TimeSpan delay, CancellationToken cancellationToken, ref Task? customTask);

    partial void OnCompletedAsyncPartial(bool cancelled, ref Task? customTask);

    partial void OnCancelledPartial(ref bool handled);
}
