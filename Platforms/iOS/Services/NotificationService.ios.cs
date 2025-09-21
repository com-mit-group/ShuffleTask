#if IOS || MACCATALYST
using System;
using System.Threading.Tasks;
using Foundation;
using UserNotifications;

namespace ShuffleTask.Services;

public partial class NotificationService
{
    partial void InitializePlatform(ref INotificationPlatform platform)
    {
        platform = new AppleNotificationPlatform();
    }

    private sealed class AppleNotificationPlatform : INotificationPlatform
    {
        public async Task InitializeAsync()
        {
            var center = UNUserNotificationCenter.Current;
            var settings = await center.GetNotificationSettingsAsync();
            if (settings.AuthorizationStatus == UNAuthorizationStatus.NotDetermined)
            {
                await center.RequestAuthorizationAsync(UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound);
            }
        }

        public Task NotifyAsync(string title, string message, TimeSpan delay, bool playSound)
            => ScheduleAsync(title, message, delay, playSound);

        public Task ShowToastAsync(string title, string message, bool playSound)
            => ScheduleAsync(title, message, TimeSpan.Zero, playSound);

        private const double MinimumTriggerDelaySeconds = 0.1;

        private static async Task ScheduleAsync(string title, string message, TimeSpan delay, bool playSound)
        {
            var content = new UNMutableNotificationContent
            {
                Title = title,
                Body = message
            };

            if (playSound)
            {
                content.Sound = UNNotificationSound.Default;
            }

            double seconds = Math.Max(MinimumTriggerDelaySeconds, delay.TotalSeconds);
            var trigger = UNTimeIntervalNotificationTrigger.CreateTrigger(seconds, repeats: false);
            var request = UNNotificationRequest.FromIdentifier(Guid.NewGuid().ToString(), content, trigger);

            await UNUserNotificationCenter.Current.AddNotificationRequestAsync(request);
        }
    }
}
#endif
