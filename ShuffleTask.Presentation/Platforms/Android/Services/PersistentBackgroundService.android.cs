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
using Microsoft.Extensions.DependencyInjection;
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

        private static void EnsureForegroundService()
        {
            var context = Android.App.Application.Context;
            if (!PersistentBackgroundAndroidService.IsRunning)
            {
                PersistentBackgroundAndroidService.Start(context);
            }
        }

        private static PendingIntent? CreatePendingIntent(Context context, PendingIntentFlags baseFlags)
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
            var intent = new Intent(context, typeof(global::ShuffleTask.MainActivity));
            intent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);

            var flags = PendingIntentFlags.UpdateCurrent;
            if (OperatingSystem.IsAndroidVersionAtLeast(23))
            {
                flags |= PendingIntentFlags.Immutable;
            }

            var pendingIntent = PendingIntent.GetActivity(context, 0, intent, flags);

            return new NotificationCompat.Builder(context, ForegroundChannelId)
                .SetContentTitle("ShuffleTask is running")
                .SetContentText("Background reminders are enabled.")
                .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
                .SetOngoing(true)
                .SetCategory(Notification.CategoryService)
                .SetContentIntent(pendingIntent)
                .Build();
        }
    }
}
#endif
