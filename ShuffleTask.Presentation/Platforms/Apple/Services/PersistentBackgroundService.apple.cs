#if IOS || MACCATALYST
using System;
using System.Threading.Tasks;
using BackgroundTasks;
using CoreFoundation;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using ShuffleTask.Presentation.Services;

namespace ShuffleTask.Presentation.Services;

internal partial class PersistentBackgroundService
{
    internal const string AppleTaskIdentifier = "com.commitgroup.shuffletask.autoshuffle";

    partial void InitializePlatform(TimeProvider clock, ref IPersistentBackgroundPlatform? platform)
    {
        platform = new ApplePersistentBackgroundPlatform();
    }

    private sealed class ApplePersistentBackgroundPlatform : IPersistentBackgroundPlatform
    {
        private bool _registered;

        public Task InitializeAsync()
        {
            EnsureRegistered();
            return Task.CompletedTask;
        }

        public void Schedule(DateTimeOffset when, TimeSpan delay, string? taskId)
        {
            EnsureRegistered();

            ExecuteOnMainThread(() =>
            {
                BGTaskScheduler.Shared.Cancel(AppleTaskIdentifier);

                var request = new BGProcessingTaskRequest(AppleTaskIdentifier)
                {
                    RequiresExternalPower = false,
                    RequiresNetworkConnectivity = false
                };

                if (delay > TimeSpan.Zero)
                {
                    request.EarliestBeginDate = NSDate.FromTimeIntervalSinceNow(delay.TotalSeconds);
                }
                else
                {
                    request.EarliestBeginDate = NSDate.Now;
                }

                try
                {
                    BGTaskScheduler.Shared.Submit(request);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ShuffleTask] Failed to submit background task: {ex}");
                }
            });
        }

        public void Cancel()
        {
            ExecuteOnMainThread(() => BGTaskScheduler.Shared.Cancel(AppleTaskIdentifier));
        }

        public void Stop()
        {
            ExecuteOnMainThread(() => BGTaskScheduler.Shared.Cancel(AppleTaskIdentifier));
        }

        private void EnsureRegistered()
        {
            if (_registered)
            {
                return;
            }

            ExecuteOnMainThread(() =>
            {
                if (_registered)
                {
                    return;
                }

                try
                {
                    BGTaskScheduler.Shared.Register(AppleTaskIdentifier, null, HandleTask);
                    _registered = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ShuffleTask] Failed to register background task: {ex}");
                }
            });
        }

        private static void HandleTask(BGTask task)
        {
            task.ExpirationHandler = () =>
            {
                task.SetTaskCompleted(false);
            };

            Task.Run(async () =>
            {
                try
                {
                    var coordinator = MauiProgram.TryGetServiceProvider()?.GetService<ShuffleCoordinatorService>();
                    if (coordinator != null)
                    {
                        await coordinator.HandlePersistentTriggerAsync().ConfigureAwait(false);
                        task.SetTaskCompleted(true);
                    }
                    else
                    {
                        task.SetTaskCompleted(false);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ShuffleTask] Background task error: {ex}");
                    task.SetTaskCompleted(false);
                }
            });
        }

        private static void ExecuteOnMainThread(Action action)
        {
            if (action is null)
            {
                return;
            }

            if (NSThread.IsMain)
            {
                action();
            }
            else
            {
                DispatchQueue.MainQueue.DispatchSync(action);
            }
        }
    }
}
#endif
