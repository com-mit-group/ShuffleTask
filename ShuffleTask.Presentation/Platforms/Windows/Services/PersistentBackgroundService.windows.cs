#if WINDOWS
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace ShuffleTask.Presentation.Services;

public partial class PersistentBackgroundService
{
    partial void InitializePlatform(TimeProvider clock, ref IPersistentBackgroundPlatform platform)
    {
        platform = new WindowsPersistentBackgroundPlatform();
    }

    private sealed class WindowsPersistentBackgroundPlatform : IPersistentBackgroundPlatform
    {
        private readonly object _sync = new();
        private CancellationTokenSource? _cts;

        public Task InitializeAsync() => Task.CompletedTask;

        public void Schedule(DateTimeOffset when, TimeSpan delay, string? taskId)
        {
            lock (_sync)
            {
                _ = when;
                _ = taskId;
                CancelLocked();

                var cts = new CancellationTokenSource();
                _cts = cts;
                _ = RunAsync(delay, cts.Token);
            }
        }

        public void Cancel()
        {
            lock (_sync)
            {
                CancelLocked();
            }
        }

        private void CancelLocked()
        {
            if (_cts != null)
            {
                try
                {
                    _cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Timer was already disposed; ignore.
                }
                finally
                {
                    _cts.Dispose();
                    _cts = null;
                }
            }
        }

        private static async Task RunAsync(TimeSpan delay, CancellationToken token)
        {
            try
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                var coordinator = MauiProgram.TryGetServiceProvider()?.GetService<ShuffleCoordinatorService>();
                if (coordinator != null)
                {
                    await coordinator.HandlePersistentTriggerAsync().ConfigureAwait(false);
                }
            }
            catch (TaskCanceledException)
            {
                // Timer canceled before execution; nothing to do.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ShuffleTask Windows background timer error: {ex}");
            }
        }
    }
}
#endif
