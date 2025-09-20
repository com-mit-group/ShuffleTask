using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using ShuffleTask.ViewModels;

namespace ShuffleTask.Views
{
    public partial class DashboardPage : ContentPage
    {
        private readonly DashboardViewModel _vm;
        private readonly IDispatcherTimer _timer;
        private TimeSpan _remaining;

        private const string PrefTaskId = "pref.currentTaskId";
        private const string PrefRemainingSecs = "pref.remainingSecs";

        public DashboardPage(DashboardViewModel vm)
        {
            InitializeComponent();
            BindingContext = _vm = vm;

            _timer = Application.Current?.Dispatcher.CreateTimer() ?? Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += OnTick;

            Loaded += NowPage_Loaded;

            //ShuffleButton.Clicked += async (s, e) => await StartShuffleAsync();

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
                _remaining = TimeSpan.FromSeconds(secs);
                _vm.CountdownText = $"{_remaining:mm\\:ss}";
                _timer.Start();
                return;
            }
        }

        private async Task StartShuffleAsync()
        {
            var minutes = await _vm.Shuffle();
            if (_vm.CurrentTask == null)
            {
                // No task available now, do not start timer
                return;
            }

            await BeginCountdownAsync(minutes);
        }

        public async Task BeginCountdownAsync(int minutes)
        {
            _remaining = TimeSpan.FromMinutes(minutes);
            _vm.CountdownText = $"{_remaining:mm\\:ss}";
            PersistState();
            _timer.Stop();
            _timer.Start();

            await _vm.NotifyCurrentTaskAsync(minutes);
        }

        private async void OnTick(object? sender, EventArgs e)
        {
            if (_remaining.TotalSeconds <= 0)
            {
                _timer.Stop();
                Preferences.Default.Remove(PrefRemainingSecs);
                await _vm.TimeUpAsync();
                await StartShuffleAsync();
                return;
            }

            _remaining -= TimeSpan.FromSeconds(1);
            _vm.CountdownText = $"{_remaining:mm\\:ss}";
            PersistState();
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

        private void OnCompleteOrSkip()
        {
            _timer.Stop();
            _remaining = TimeSpan.Zero;
            _vm.CountdownText = "00:00";
            _vm.CurrentTask = null;
            ClearPersistedState();
        }
    }
}
