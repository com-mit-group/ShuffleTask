using System;
using System.Threading;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Microsoft.Maui.ApplicationModel;

namespace ShuffleTask.Services;

public partial class NotificationService
{
    private const string AndroidNotificationAction = "com.companyname.shuffletask.SHOW_NOTIFICATION";
    private const string AndroidNotificationExtraTitle = "ShuffleTask.Notification.Title";
    private const string AndroidNotificationExtraMessage = "ShuffleTask.Notification.Message";
    private const string AndroidNotificationExtraSound = "ShuffleTask.Notification.Sound";
    private const string AndroidNotificationExtraId = "ShuffleTask.Notification.Id";

    private const string SoundChannelId = "shuffletask.reminders.sound";
    private const string SilentChannelId = "shuffletask.reminders.silent";
    private const string SoundChannelName = "ShuffleTask reminders";
    private const string SilentChannelName = "ShuffleTask silent reminders";
    private const string ChannelDescription = "Task reminders and timer alerts.";

    private static int _nextAndroidNotificationId = 2000;

    partial void InitializePlatform(ref INotificationPlatform platform)
    {
        platform = new AndroidNotificationPlatform();
    }

    private static int GetNextAndroidNotificationId()
        => Interlocked.Increment(ref _nextAndroidNotificationId);

    private static void EnsureChannels(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        if (context.GetSystemService(Context.NotificationService) is not NotificationManager manager)
        {
            return;
        }

        if (manager.GetNotificationChannel(SoundChannelId) is null)
        {
            var channel = new NotificationChannel(SoundChannelId, SoundChannelName, NotificationImportance.Default)
            {
                Description = ChannelDescription
            };
            channel.EnableVibration(true);
            manager.CreateNotificationChannel(channel);
        }

        if (manager.GetNotificationChannel(SilentChannelId) is null)
        {
            var channel = new NotificationChannel(SilentChannelId, SilentChannelName, NotificationImportance.Default)
            {
                Description = ChannelDescription
            };
            channel.EnableVibration(false);
            channel.SetSound(null, null);
            manager.CreateNotificationChannel(channel);
        }
    }

    private static void PostAndroidNotification(Context context, string title, string message, bool playSound, int notificationId)
    {
        EnsureChannels(context);

        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);

        var flags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            flags |= PendingIntentFlags.Immutable;
        }

        var pendingIntent = PendingIntent.GetActivity(
            context,
            requestCode: 0,
            intent,
            flags);

        string channelId = playSound ? SoundChannelId : SilentChannelId;

        var builder = new NotificationCompat.Builder(context, channelId)
            .SetContentTitle(title)
            .SetContentText(message)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(message))
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
            .SetAutoCancel(true)
            .SetPriority(NotificationCompat.PriorityHigh)
            .SetCategory(NotificationCompat.CategoryReminder)
            .SetContentIntent(pendingIntent);

        if (playSound)
        {
            builder.SetDefaults(NotificationCompat.DefaultAll);
        }
        else
        {
            builder.SetSilent(true);
        }

        NotificationManagerCompat.From(context).Notify(notificationId, builder.Build());
    }

    private static void ScheduleAndroidNotification(Context context, string title, string message, TimeSpan delay, bool playSound, int notificationId)
    {
        EnsureChannels(context);

        long delayMs = (long)Math.Max(0, delay.TotalMilliseconds);
        if (delayMs == 0)
        {
            PostAndroidNotification(context, title, message, playSound, notificationId);
            return;
        }

        var intent = new Intent(context, typeof(ReminderBroadcastReceiver))
            .SetAction(AndroidNotificationAction)
            .PutExtra(AndroidNotificationExtraTitle, title)
            .PutExtra(AndroidNotificationExtraMessage, message)
            .PutExtra(AndroidNotificationExtraSound, playSound)
            .PutExtra(AndroidNotificationExtraId, notificationId);

        var flags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            flags |= PendingIntentFlags.Immutable;
        }

        var pendingIntent = PendingIntent.GetBroadcast(
            context,
            notificationId,
            intent,
            flags);

        if (pendingIntent == null)
        {
            PostAndroidNotification(context, title, message, playSound, notificationId);
            return;
        }

        if (context.GetSystemService(Context.AlarmService) is AlarmManager alarmManager)
        {
            long triggerAt = SystemClock.ElapsedRealtime() + delayMs;
            if (OperatingSystem.IsAndroidVersionAtLeast(23))
            {
                alarmManager.SetExactAndAllowWhileIdle(AlarmType.ElapsedRealtimeWakeup, triggerAt, pendingIntent);
            }
            else
            {
                alarmManager.SetExact(AlarmType.ElapsedRealtimeWakeup, triggerAt, pendingIntent);
            }
        }
        else
        {
            PostAndroidNotification(context, title, message, playSound, notificationId);
        }
    }

    private sealed class AndroidNotificationPlatform : INotificationPlatform
    {
        public Task InitializeAsync()
        {
            var context = Android.App.Application.Context;
            EnsureChannels(context);

            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                try
                {
                    var activity = Platform.CurrentActivity;
                    if (activity != null &&
                        ContextCompat.CheckSelfPermission(activity, Android.Manifest.Permission.PostNotifications) != (int)Permission.Granted)
                    {
                        ActivityCompat.RequestPermissions(activity, new[] { Android.Manifest.Permission.PostNotifications }, 0x42);
                    }
                }
                catch
                {
                    // Ignore permission request failures. The OS dialog may not be available if called too early.
                }
            }

            return Task.CompletedTask;
        }

        public Task NotifyAsync(string title, string message, TimeSpan delay, bool playSound)
        {
            var context = Android.App.Application.Context;
            int notificationId = GetNextAndroidNotificationId();

            if (delay <= TimeSpan.Zero)
            {
                PostAndroidNotification(context, title, message, playSound, notificationId);
            }
            else
            {
                ScheduleAndroidNotification(context, title, message, delay, playSound, notificationId);
            }

            return Task.CompletedTask;
        }

        public Task ShowToastAsync(string title, string message, bool playSound)
        {
            var context = Android.App.Application.Context;
            PostAndroidNotification(context, title, message, playSound, GetNextAndroidNotificationId());
            return Task.CompletedTask;
        }
    }

    [BroadcastReceiver(Enabled = true, Exported = true, Name = "com.companyname.shuffletask.ReminderBroadcastReceiver")]
    [IntentFilter(new[] { AndroidNotificationAction })]
    private sealed class ReminderBroadcastReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context == null || intent == null)
            {
                return;
            }

            if (!string.Equals(intent.Action, AndroidNotificationAction, StringComparison.Ordinal))
            {
                return;
            }

            string title = intent.GetStringExtra(AndroidNotificationExtraTitle) ?? "ShuffleTask";
            string message = intent.GetStringExtra(AndroidNotificationExtraMessage) ?? string.Empty;
            bool playSound = intent.GetBooleanExtra(AndroidNotificationExtraSound, true);
            int notificationId = intent.GetIntExtra(AndroidNotificationExtraId, GetNextAndroidNotificationId());

            PostAndroidNotification(context, title, message, playSound, notificationId);
        }
    }
}
