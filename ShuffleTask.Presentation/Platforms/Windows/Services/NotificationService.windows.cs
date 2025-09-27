#if WINDOWS
using System;
using System.Threading.Tasks;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace ShuffleTask.Services;

public partial class NotificationService
{
    partial void InitializePlatform(ref INotificationPlatform platform)
    {
        try
        {
            var notifier = ToastNotificationManager.CreateToastNotifier();
            platform = new WindowsNotificationPlatform(notifier, _clock);
        }
        catch
        {
            // Fall back to the default in-app alerts if toast notifications are unavailable.
        }
    }

        private sealed class WindowsNotificationPlatform : INotificationPlatform
        {
            private static readonly TimeSpan MinimumScheduleDelay = TimeSpan.FromSeconds(1);
            private readonly ToastNotifier _notifier;
            private readonly TimeProvider _clock;

        public WindowsNotificationPlatform(ToastNotifier notifier, TimeProvider clock)
        {
            _notifier = notifier;
            _clock = clock;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task NotifyAsync(string title, string message, TimeSpan delay, bool playSound)
        {
            bool success = delay <= TimeSpan.Zero
                ? TryShowToast(title, message, playSound)
                : TryScheduleToast(title, message, delay, playSound);

            if (!success)
            {
                await ShowAlertAsync(title, message);
            }
        }

        public async Task ShowToastAsync(string title, string message, bool playSound)
        {
            if (!TryShowToast(title, message, playSound))
            {
                await ShowAlertAsync(title, message);
            }
        }

        private bool TryShowToast(string title, string message, bool playSound)
        {
            try
            {
                if (_notifier.Setting != NotificationSetting.Enabled)
                {
                    return false;
                }

                var toast = new ToastNotification(CreateToastXml(title, message, playSound));
                _notifier.Show(toast);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryScheduleToast(string title, string message, TimeSpan delay, bool playSound)
        {
            try
            {
                if (_notifier.Setting != NotificationSetting.Enabled)
                {
                    return false;
                }

                var xml = CreateToastXml(title, message, playSound);
                DateTimeOffset baseTime = _clock.GetLocalNow();
                var effectiveDelay = delay < MinimumScheduleDelay ? MinimumScheduleDelay : delay;
                var deliveryTime = baseTime + effectiveDelay;
                var scheduled = new ScheduledToastNotification(xml, deliveryTime);
                _notifier.AddToSchedule(scheduled);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static XmlDocument CreateToastXml(string title, string message, bool playSound)
        {
            var xml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var textNodes = xml.GetElementsByTagName("text");

            if (textNodes.Length > 0)
            {
                textNodes[0].AppendChild(xml.CreateTextNode(title));
            }

            if (textNodes.Length > 1)
            {
                textNodes[1].AppendChild(xml.CreateTextNode(message));
            }

            if (!playSound)
            {
                var audioElement = xml.CreateElement("audio");
                audioElement.SetAttribute("silent", "true");
                xml.DocumentElement?.AppendChild(audioElement);
            }

            return xml;
        }
    }
}
#endif
