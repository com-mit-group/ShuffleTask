#if ANDROID
using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using AndroidX.Core.App;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Presentation.Services;

namespace ShuffleTask.Presentation.Services;

internal partial class PersistentBackgroundService
{
    private const string AlarmAction = "com.commitgroup.shuffletask.SHUFFLE_ALARM";
    private const int AlarmRequestCode = 0x7011;
    private const string ForegroundChannelId = "shuffletask.background";
    private const string ForegroundChannelName = "ShuffleTask background";
    private const string ForegroundChannelDescription = "Keeps ShuffleTask timers active for reminders.";
    private const int ForegroundNotificationId = 0x7012;
    private const string StopBackgroundAction = "com.commitgroup.shuffletask.STOP_BACKGROUND_ACTIVITY";
    private const int StopBackgroundActionRequestCode = 0x7013;

    partial void InitializePlatform(TimeProvider clock, ref IPersistentBackgroundPlatform? platform)
    {
        platform = new AndroidPersistentBackgroundPlatform();
    }

    private sealed class AndroidPersistentBackgroundPlatform : IPersistentBackgroundPlatform
    {
        public Task InitializeAsync()
        {
            EnsureForegroundService();
            return Task.CompletedTask;
        }

        public void Schedule(DateTimeOffset when, TimeSpan delay, string? taskId)
        {
            var context = Android.App.Application.Context;
            EnsureForegroundService();
            var pendingIntent = CreatePendingIntent(context, PendingIntentFlags.UpdateCurrent);
            if (pendingIntent == null)
            {
                return;
            }

            long triggerAt = SystemClock.ElapsedRealtime() + (long)Math.Max(0, delay.TotalMilliseconds);
            if (context.GetSystemService(Context.AlarmService) is AlarmManager alarmManager)
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(23))
                {
                    alarmManager.SetExactAndAllowWhileIdle(AlarmType.ElapsedRealtimeWakeup, triggerAt, pendingIntent);
                }
                else
                {
                    alarmManager.SetExact(AlarmType.ElapsedRealtimeWakeup, triggerAt, pendingIntent);
                }
            }
        }

        public void Cancel()
        {
            var context = Android.App.Application.Context;
            var pendingIntent = CreatePendingIntent(context, PendingIntentFlags.NoCreate);
            if (pendingIntent == null)
            {
                return;
            }

            if (context.GetSystemService(Context.AlarmService) is AlarmManager alarmManager)
            {
                alarmManager.Cancel(pendingIntent);
            }

            pendingIntent.Cancel();
        }

        public void Stop()
        {
            var context = Android.App.Application.Context;
            var intent = new Intent(context, typeof(PersistentBackgroundAndroidService));
            context.StopService(intent);
        }

        private static void EnsureForegroundService()
        {
            var context = Android.App.Application.Context;
            if (!PersistentBackgroundAndroidService.IsRunning)
            {
                PersistentBackgroundAndroidService.Start(context);
            }
        }

        private static PendingIntent? CreatePendingIntent(Context context, PendingIntentFlags baseFlags)
            => CreateAlarmPendingIntent(context, baseFlags);
    }

    [BroadcastReceiver(Enabled = true, Exported = true, Name = "com.commitgroup.shuffletask.ShuffleCoordinatorAlarmReceiver")]
    [IntentFilter(new[] { AlarmAction })]
    private sealed class ShuffleCoordinatorAlarmReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent == null || !string.Equals(intent.Action, AlarmAction, StringComparison.Ordinal))
            {
                return;
            }

            var pendingResult = GoAsync();

            _ = Task.Run(async () =>
            {
                try
                {
                    var coordinator = MauiProgram.TryGetServiceProvider()?.GetService<ShuffleCoordinatorService>();
                    if (coordinator != null)
                    {
                        await coordinator.HandlePersistentTriggerAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("ShuffleTask", $"Alarm receiver error: {ex}");
                }
                finally
                {
                    pendingResult?.Finish();
                }
            });
        }
    }

    [BroadcastReceiver(Enabled = true, Exported = false, Name = "com.commitgroup.shuffletask.StopBackgroundActionReceiver")]
    [IntentFilter(new[] { StopBackgroundAction })]
    private sealed class StopBackgroundActionReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context == null || intent == null)
            {
                return;
            }

            if (!string.Equals(intent.Action, StopBackgroundAction, StringComparison.Ordinal))
            {
                return;
            }

            var pendingResult = GoAsync();

            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleStopBackgroundActionAsync(context).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Warn("ShuffleTask", $"Stop background action error: {ex}");
                }
                finally
                {
                    pendingResult?.Finish();
                }
            });
        }
    }

    [Service(Enabled = true, Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync, Name = "com.commitgroup.shuffletask.PersistentBackgroundService")]
    private sealed class PersistentBackgroundAndroidService : Service
    {
        private static bool _running;

        public static bool IsRunning => Volatile.Read(ref _running);

        public static void Start(Context context)
        {
            var intent = new Intent(context, typeof(PersistentBackgroundAndroidService));

            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                context.StartForegroundService(intent);
            }
            else
            {
                context.StartService(intent);
            }
        }

        public static void Stop(Context context)
        {
            var intent = new Intent(context, typeof(PersistentBackgroundAndroidService));
            context.StopService(intent);
        }

        public override void OnCreate()
        {
            base.OnCreate();
            Volatile.Write(ref _running, true);
            EnsureChannel();
        }

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
            StartForeground(ForegroundNotificationId, BuildNotification());
            return StartCommandResult.Sticky;
        }

        public override void OnDestroy()
        {
            StopForeground(true);
            Volatile.Write(ref _running, false);
            base.OnDestroy();
        }

        public override IBinder? OnBind(Intent? intent) => null;

        private static void EnsureChannel()
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                return;
            }

            var context = Android.App.Application.Context;
            if (context.GetSystemService(Context.NotificationService) is not NotificationManager manager)
            {
                return;
            }

            if (manager.GetNotificationChannel(ForegroundChannelId) != null)
            {
                return;
            }

            var channel = new NotificationChannel(ForegroundChannelId, ForegroundChannelName, NotificationImportance.Low)
            {
                Description = ForegroundChannelDescription
            };

            manager.CreateNotificationChannel(channel);
        }

        private Notification BuildNotification()
        {
            var context = Android.App.Application.Context;
            var intent = new Intent(context, typeof(global::ShuffleTask.Presentation.MainActivity));
            intent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);

            var flags = PendingIntentFlags.UpdateCurrent;
            if (OperatingSystem.IsAndroidVersionAtLeast(23))
            {
                flags |= PendingIntentFlags.Immutable;
            }

            var pendingIntent = PendingIntent.GetActivity(context, 0, intent, flags);

            var stopActionIntent = new Intent(context, typeof(StopBackgroundActionReceiver))
                .SetAction(StopBackgroundAction);

            var stopActionPendingIntent = PendingIntent.GetBroadcast(
                context,
                StopBackgroundActionRequestCode,
                stopActionIntent,
                flags);

            return new NotificationCompat.Builder(context, ForegroundChannelId)
                .SetContentTitle("ShuffleTask is running")
                .SetContentText("Background reminders are enabled.")
                .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
                .SetOngoing(true)
                .SetCategory(Notification.CategoryService)
                .SetContentIntent(pendingIntent)
                .AddAction(new NotificationCompat.Action.Builder(
                    Android.Resource.Drawable.IcMenuCloseClearCancel,
                    "Stop background activity.",
                    stopActionPendingIntent).Build())
                .Build();
        }
    }

    private static PendingIntent? CreateAlarmPendingIntent(Context context, PendingIntentFlags baseFlags)
    {
        var intent = new Intent(context, typeof(ShuffleCoordinatorAlarmReceiver))
            .SetAction(AlarmAction);

        PendingIntentFlags flags = baseFlags;
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            flags |= PendingIntentFlags.Immutable;
        }

        return PendingIntent.GetBroadcast(context, AlarmRequestCode, intent, flags);
    }

    private static void CancelAlarmOnly(Context context)
    {
        var pendingIntent = CreateAlarmPendingIntent(context, PendingIntentFlags.NoCreate);
        if (pendingIntent == null)
        {
            return;
        }

        if (context.GetSystemService(Context.AlarmService) is AlarmManager alarmManager)
        {
            alarmManager.Cancel(pendingIntent);
        }

        pendingIntent.Cancel();
    }

    private static async Task HandleStopBackgroundActionAsync(Context context)
    {
        NotificationManagerCompat.From(context).CancelAll();

        var services = MauiProgram.TryGetServiceProvider();
        if (services == null)
        {
            Log.Info("ShuffleTask", "Foreground notification action invoked: stop background activity.");
            CancelAlarmOnly(context);
            PersistentBackgroundAndroidService.Stop(context);
            return;
        }

        var logger = services.GetService<ILogger<StopBackgroundActionReceiver>>();
        logger?.LogInformation("Foreground notification action invoked: stop background activity.");

        var settings = services.GetService<AppSettings>();
        var storage = services.GetService<IStorageService>();
        var coordinator = services.GetService<ShuffleCoordinatorService>();
        var clock = services.GetService<TimeProvider>() ?? TimeProvider.System;

        if (settings != null)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                settings.BackgroundActivityEnabled = false;
                settings.Touch(clock);
            }).ConfigureAwait(false);

            if (storage != null)
            {
                await storage.SetSettingsAsync(settings).ConfigureAwait(false);
            }
        }

        if (coordinator != null)
        {
            await coordinator.ApplyBackgroundActivityChangeAsync(false).ConfigureAwait(false);
            return;
        }

        CancelAlarmOnly(context);
        PersistentBackgroundAndroidService.Stop(context);
    }
}
#endif
