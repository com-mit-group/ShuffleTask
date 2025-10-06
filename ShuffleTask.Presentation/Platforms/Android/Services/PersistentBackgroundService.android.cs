#if ANDROID
using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;
using Microsoft.Extensions.DependencyInjection;
using ShuffleTask.Presentation.Services;

namespace ShuffleTask.Presentation.Services;

public partial class PersistentBackgroundService
{
    private const string AlarmAction = "com.companyname.shuffletask.SHUFFLE_ALARM";
    private const int AlarmRequestCode = 0x7011;

    partial void InitializePlatform(TimeProvider clock, ref IPersistentBackgroundPlatform platform)
    {
        platform = new AndroidPersistentBackgroundPlatform();
    }

    private sealed class AndroidPersistentBackgroundPlatform : IPersistentBackgroundPlatform
    {
        public Task InitializeAsync() => Task.CompletedTask;

        public void Schedule(DateTimeOffset when, TimeSpan delay, string? taskId)
        {
            var context = Android.App.Application.Context;
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

    [BroadcastReceiver(Enabled = true, Exported = true, Name = "com.companyname.shuffletask.ShuffleCoordinatorAlarmReceiver")]
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
}
#endif
