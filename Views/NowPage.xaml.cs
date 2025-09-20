using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using ShuffleTask.ViewModels;

namespace ShuffleTask.Views
{
    public partial class NowPage : ContentPage
    {
        private readonly NowViewModel _vm;
        private readonly IDispatcherTimer _timer;
        private TimeSpan _remaining;

        private const string PrefTaskId = "pref.currentTaskId";
        private const string PrefRemainingSecs = "pref.remainingSecs";

        public NowPage(NowViewModel vm)
        {
            InitializeComponent();
            BindingContext = _vm = vm;

            _timer = Application.Current?.Dispatcher.CreateTimer() ?? Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.IsRepeating = true;
            _timer.Tick += OnTick;

            Loaded += NowPage_Loaded;

            ShuffleButton.Clicked += async (s, e) => await StartShuffleAsync();

            // Stop countdown and clear persisted state when user completes or skips
            _vm.DoneOccurred += (_, __) => OnCompleteOrSkip();
            _vm.SkipOccurred += (_, __) => OnCompleteOrSkip();
        }

        private async void NowPage_Loaded(object? sender, EventArgs e)
        {
            await _vm.InitializeAsync();

            // If no task picked/persisted, don't start timer
            var secs = Preferences.Default.Get(PrefRemainingSecs, -1);
            var id = Preferences.Default.Get(PrefTaskId, string.Empty);
            if (secs > 0 && !string.IsNullOrEmpty(id))
            {
                if (await _vm.LoadTaskByIdAsync(id))
                {
                    var remaining = TimeSpan.FromSeconds(secs);
                    await StartCountdownForCurrentTaskAsync(remaining);
                    await StartCountdownAsync(remaining, sendNotification: false);
                    return;
                }

                ClearPersistedState();
            }
        }

        private async Task StartShuffleAsync()
        {
            var minutes = await _vm.ShuffleAsync(DateTime.Now);
            if (_vm.CurrentTask == null)
            {
                // No task available now, do not start timer
                ResetCountdown();
                return;
            }

            await StartCountdownForCurrentTaskAsync(TimeSpan.FromMinutes(minutes), notify: true);
            await StartCountdownAsync(TimeSpan.FromMinutes(minutes), sendNotification: false);
        }

        public Task BeginCountdownAsync(int minutes) =>
            StartCountdownAsync(TimeSpan.FromMinutes(minutes), sendNotification: true);

        private async Task StartCountdownAsync(TimeSpan remaining, bool sendNotification)
        {
            await StartCountdownForCurrentTaskAsync(TimeSpan.FromMinutes(minutes), notify: true);
            if (_vm.CurrentTask == null)
            {
                return;
            }

            _timer.Stop();
            _remaining = remaining;
            _vm.CountdownText = FormatCountdown(_remaining);
            PersistState();

            if (_remaining > TimeSpan.Zero)
            {
                _timer.Start();
            }

            if (sendNotification)
            {
                int notifyMinutes = Math.Max(1, (int)Math.Ceiling(_remaining.TotalMinutes));
                await _vm.NotifyCurrentTaskAsync(notifyMinutes);
            }
        }

        private async void OnTick(object? sender, EventArgs e)
        {
            if (_remaining.TotalSeconds <= 0)
            {
                _timer.Stop();
                ClearPersistedState();
                await _vm.TimeUpAsync();
                await StartShuffleAsync();
                return;
            }

            _remaining -= TimeSpan.FromSeconds(1);
            _vm.CountdownText = FormatCountdown(_remaining);
            PersistState();
        }

        private static string FormatCountdown(TimeSpan remaining)
        {
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            int minutes = Math.Max(0, (int)remaining.TotalMinutes);
            int seconds = remaining.Seconds;
            return $"{minutes:D2}:{seconds:D2}";
        }

        private async Task StartCountdownForCurrentTaskAsync(TimeSpan remaining, bool notify = false)
        {
            if (_vm.CurrentTask == null)
            {
                ResetCountdown();
                return;
            }

            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            _timer.Stop();
            _remaining = remaining;
            _vm.CountdownText = FormatCountdown(_remaining);
            PersistState();

            if (_remaining > TimeSpan.Zero)
            {
                _timer.Start();
            }

            if (notify && _remaining > TimeSpan.Zero)
            {
                int notifyMinutes = Math.Max(1, (int)Math.Ceiling(_remaining.TotalMinutes));
                await _vm.NotifyCurrentTaskAsync(notifyMinutes);
            }
        }

        private static string FormatCountdown(TimeSpan remaining)
        {
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            int minutes = Math.Max(0, (int)remaining.TotalMinutes);
            int seconds = remaining.Seconds;
            return $"{minutes:D2}:{seconds:D2}";
        }

        private void PersistState()
        {
            var id = _vm.CurrentTask?.Id ?? string.Empty;
            Preferences.Default.Set(PrefTaskId, id);
            Preferences.Default.Set(PrefRemainingSecs, (int)_remaining.TotalSeconds);
        }

        private void ClearPersistedState()
        {
            Preferences.Default.Remove(PrefTaskId);
            Preferences.Default.Remove(PrefRemainingSecs);
        }

        private void ResetCountdown()
        {
            _timer.Stop();
            _remaining = TimeSpan.Zero;
            _vm.CountdownText = "00:00";
            ClearPersistedState();
        }

        private void OnCompleteOrSkip()
        {
            _vm.CurrentTask = null;
            ResetCountdown();
        }
    }
}
