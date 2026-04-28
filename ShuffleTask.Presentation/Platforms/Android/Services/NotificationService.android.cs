using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Microsoft.Maui.ApplicationModel;
using ShuffleTask.Presentation.Utilities;

namespace ShuffleTask.Presentation.Services;

public partial class NotificationService
{
    private const string AndroidNotificationAction = "com.commitgroup.shuffletask.SHOW_NOTIFICATION";
    private const string AndroidNotificationExtraTitle = "ShuffleTask.Notification.TimeUpTitle";
    private const string AndroidNotificationExtraMessage = "ShuffleTask.Notification.TimeUpMessage";
    private const string AndroidNotificationExtraSound = "ShuffleTask.Notification.Sound";
    private const string AndroidNotificationExtraId = "ShuffleTask.Notification.Id";
    private const string AndroidNotificationExtraAlignWithActiveTimer = "ShuffleTask.Notification.AlignWithActiveTimer";
    private const string AndroidNotificationExtraAlignedTimerTaskId = "ShuffleTask.Notification.AlignedTimerTaskId";
    private const string AndroidNotificationExtraAlignedTimerExpiresAtUtc = "ShuffleTask.Notification.AlignedTimerExpiresAtUtc";

    private const string SoundChannelId = "shuffletask.reminders.sound";
    private const string SilentChannelId = "shuffletask.reminders.silent";
    private const string SoundChannelName = "ShuffleTask reminders";
    private const string SilentChannelName = "ShuffleTask silent reminders";
    private const string ChannelDescription = "Task reminders and timer alerts.";
    private const int NotificationPermissionRequestCode = 0x42;
    private const int TimerAlignmentToleranceSeconds = 5;

    private static int _nextAndroidNotificationId = 2000;
    private static readonly ConcurrentDictionary<int, byte> ScheduledNotificationIds = new();

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

    private static void ScheduleAndroidNotification(
        Context context,
        string title,
        string message,
        TimeSpan delay,
        bool playSound,
        int notificationId,
        bool alignWithActiveTimer,
        string alignedTimerTaskId,
        string alignedTimerExpiresAtUtc)
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
            .PutExtra(AndroidNotificationExtraId, notificationId)
            .PutExtra(AndroidNotificationExtraAlignWithActiveTimer, alignWithActiveTimer)
            .PutExtra(AndroidNotificationExtraAlignedTimerTaskId, alignedTimerTaskId)
            .PutExtra(AndroidNotificationExtraAlignedTimerExpiresAtUtc, alignedTimerExpiresAtUtc);

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

        ScheduledNotificationIds[notificationId] = 0;

        if (context.GetSystemService(Context.AlarmService) is AlarmManager)
        {
            long triggerAt = SystemClock.ElapsedRealtime() + delayMs;
            DateTimeOffset scheduledFireAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(delayMs);
            System.Diagnostics.Debug.WriteLine($"NotificationService(Android): schedule id={notificationId}, delayMs={delayMs}, scheduledFireAtUtc={scheduledFireAtUtc:O}");
            ScheduleExactAlarm(context, AlarmType.ElapsedRealtimeWakeup, pendingIntent, triggerAt);
        }
        else
        {
            PostAndroidNotification(context, title, message, playSound, notificationId);
        }
    }

    private static void ScheduleExactAlarm(Context ctx, AlarmType alarmType, PendingIntent pi, long triggerMillis)
    {
        var alarmManager = (AlarmManager)ctx.GetSystemService(Context.AlarmService);
        if (alarmManager == null)
        {
            return;
        }

        if (!alarmManager.CanScheduleExactAlarms())
        {
            var intent = new Intent(Settings.ActionRequestScheduleExactAlarm);
            intent.SetFlags(ActivityFlags.NewTask);
            ctx.StartActivity(intent);
            return;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            alarmManager.SetExactAndAllowWhileIdle(alarmType, triggerMillis, pi);
        }
        else
        {
            alarmManager.SetExact(alarmType, triggerMillis, pi);
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
                        ActivityCompat.RequestPermissions(activity, new[] { Android.Manifest.Permission.PostNotifications }, NotificationPermissionRequestCode);
                    }
                }
                catch
                {
                    // Ignore permission request failures. The OS dialog may not be available if called too early.
                }
            }

            return Task.CompletedTask;
        }

        public Task CancelAllAsync()
        {
            var context = Android.App.Application.Context;
            if (context.GetSystemService(Context.AlarmService) is AlarmManager alarmManager)
            {
                foreach (int notificationId in ScheduledNotificationIds.Keys)
                {
                    var intent = new Intent(context, typeof(ReminderBroadcastReceiver))
                        .SetAction(AndroidNotificationAction);

                    var flags = PendingIntentFlags.NoCreate;
                    if (OperatingSystem.IsAndroidVersionAtLeast(23))
                    {
                        flags |= PendingIntentFlags.Immutable;
                    }

                    var pendingIntent = PendingIntent.GetBroadcast(context, notificationId, intent, flags);
                    if (pendingIntent != null)
                    {
                        alarmManager.Cancel(pendingIntent);
                        pendingIntent.Cancel();
                    }

                    ScheduledNotificationIds.TryRemove(notificationId, out _);
                }
            }

            NotificationManagerCompat.From(context).CancelAll();
            return Task.CompletedTask;
        }

        public Task NotifyAsync(string title, string message, TimeSpan delay, bool playSound)
        {
            var context = Android.App.Application.Context;
            int notificationId = GetNextAndroidNotificationId();
            ScheduledNotificationIds[notificationId] = 0;
            bool alignWithActiveTimer = false;
            string alignedTimerTaskId = string.Empty;
            string alignedTimerExpiresAtUtc = string.Empty;

            if (delay > TimeSpan.Zero
                && PersistedTimerState.TryGetActiveTimer(out string taskId, out TimeSpan remaining, out bool expired, out _, out DateTimeOffset expiresAt)
                && !expired
                && remaining > TimeSpan.Zero)
            {
                alignWithActiveTimer = Math.Abs((remaining - delay).TotalSeconds) <= 1;
                if (alignWithActiveTimer)
                {
                    alignedTimerTaskId = taskId;
                    alignedTimerExpiresAtUtc = expiresAt.ToString("O", CultureInfo.InvariantCulture);
                }
            }

            if (delay <= TimeSpan.Zero)
            {
                PostAndroidNotification(context, title, message, playSound, notificationId);
            }
            else
            {
                ScheduleAndroidNotification(
                    context,
                    title,
                    message,
                    delay,
                    playSound,
                    notificationId,
                    alignWithActiveTimer,
                    alignedTimerTaskId,
                    alignedTimerExpiresAtUtc);
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

    [BroadcastReceiver(Enabled = true, Exported = true, Name = "com.commitgroup.shuffletask.ReminderBroadcastReceiver")]
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
            bool alignWithActiveTimer = intent.GetBooleanExtra(AndroidNotificationExtraAlignWithActiveTimer, false);
            string alignedTimerTaskId = intent.GetStringExtra(AndroidNotificationExtraAlignedTimerTaskId) ?? string.Empty;
            string alignedTimerExpiresAtUtc = intent.GetStringExtra(AndroidNotificationExtraAlignedTimerExpiresAtUtc) ?? string.Empty;

            if (alignWithActiveTimer
                && !string.IsNullOrWhiteSpace(alignedTimerTaskId)
                && DateTimeOffset.TryParse(
                    alignedTimerExpiresAtUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset alignedTimerExpiresAt)
                && PersistedTimerState.TryGetActiveTimer(
                    out string activeTaskId,
                    out TimeSpan remaining,
                    out bool expired,
                    out _,
                    out DateTimeOffset activeExpiresAt)
                && !expired
                && remaining > TimeSpan.Zero
                && string.Equals(activeTaskId, alignedTimerTaskId, StringComparison.Ordinal)
                // Persisted expiry can drift by a few seconds as the UI updates timer state once per tick.
                // Keep early-fire protection active when both expirations are still effectively the same timer.
                && Math.Abs((activeExpiresAt - alignedTimerExpiresAt).TotalSeconds) <= TimerAlignmentToleranceSeconds)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"NotificationService(Android): receive id={notificationId} before timer completion, remaining={remaining.TotalMilliseconds:F0}ms, expiresAtUtc={activeExpiresAt:O}; rescheduling.");
                ScheduleAndroidNotification(
                    context,
                    title,
                    message,
                    remaining,
                    playSound,
                    notificationId,
                    alignWithActiveTimer: true,
                    alignedTimerTaskId,
                    alignedTimerExpiresAtUtc);
                return;
            }

            System.Diagnostics.Debug.WriteLine($"NotificationService(Android): firing id={notificationId}, nowUtc={DateTimeOffset.UtcNow:O}");
            PostAndroidNotification(context, title, message, playSound, notificationId);
        }
    }
}
