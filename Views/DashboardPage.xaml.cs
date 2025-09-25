using ShuffleTask.Models;
using ShuffleTask.ViewModels;

namespace ShuffleTask.Views;

public partial class DashboardPage : ContentPage
{
    private const string PrefTimerMode = "pref.timerMode";
    private const string PrefPomodoroPhase = "pref.pomodoro.phase";
    private const string PrefPomodoroCycle = "pref.pomodoro.cycle";
    private const string PrefPomodoroTotal = "pref.pomodoro.total";
    private const string PrefPomodoroFocus = "pref.pomodoro.focus";
    private const string PrefPomodoroBreak = "pref.pomodoro.break";
    
    private readonly DashboardViewModel _vm;

    private IDispatcherTimer? _timer;
    private TimeSpan _remaining;
    private DashboardViewModel.TimerRequest? _currentRequest;

    public DashboardPage(DashboardViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;

        Loaded += OnLoaded;
        _vm.CountdownRequested += OnCountdownRequested;
        _vm.CountdownCleared += OnCountdownCleared;
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        await _vm.InitializeAsync();

        string taskId = Preferences.Default.Get(PreferenceKeys.CurrentTaskId, string.Empty);
        int seconds = Preferences.Default.Get(PreferenceKeys.RemainingSeconds, -1);
        var mode = (TimerMode)Preferences.Default.Get(PrefTimerMode, (int)TimerMode.LongInterval);

        if (seconds > 0)
        {
            TimeSpan remaining = TimeSpan.FromSeconds(seconds);
            DashboardViewModel.TimerRequest? timerState = null;

            if (mode == TimerMode.Pomodoro)
            {
                int phaseValue = Preferences.Default.Get(PrefPomodoroPhase, 0);
                int cycle = Math.Max(1, Preferences.Default.Get(PrefPomodoroCycle, 1));
                int total = Math.Max(1, Preferences.Default.Get(PrefPomodoroTotal, 1));
                int focus = Math.Max(1, Preferences.Default.Get(PrefPomodoroFocus, 15));
                int breakMinutes = Math.Max(1, Preferences.Default.Get(PrefPomodoroBreak, 5));

                var phase = (DashboardViewModel.PomodoroPhase)Math.Clamp(phaseValue, 0, 1);
                TimeSpan duration = phase == DashboardViewModel.PomodoroPhase.Break
                    ? TimeSpan.FromMinutes(breakMinutes)
                    : TimeSpan.FromMinutes(focus);

                timerState = new DashboardViewModel.TimerRequest(
                    duration,
                    TimerMode.Pomodoro,
                    phase,
                    cycle,
                    total,
                    focus,
                    breakMinutes);
            }
            else
            {
                timerState = new DashboardViewModel.TimerRequest(
                    TimeSpan.FromSeconds(Math.Max(1, seconds)),
                    TimerMode.LongInterval,
                    null,
                    0,
                    0,
                    0,
                    0);
            }

            bool restored = await _vm.RestoreTaskAsync(taskId, remaining, timerState);
            if (restored)
            {
                _remaining = remaining;
                _currentRequest = timerState;
                var timer = EnsureTimer();
                timer.Stop();
                timer.Start();
                return;
            }
        }

        ClearPersistedState();
        _currentRequest = null;
    }

    private void OnCountdownRequested(object? sender, DashboardViewModel.TimerRequest request)
    {
        StartCountdown(request);
    }

    private void OnCountdownCleared(object? sender, EventArgs e)
    {
        StopCountdown();
    }

    private void StartCountdown(DashboardViewModel.TimerRequest request)
    {
        _currentRequest = request;
        _remaining = request.Duration;
        _vm.TimerText = DashboardViewModel.FormatTimerText(_remaining);
        PersistState();

        var timer = EnsureTimer();
        timer.Stop();
        timer.Start();
    }

    private void StopCountdown()
    {
        if (_timer != null)
        {
            _timer.Stop();
        }

        _remaining = TimeSpan.Zero;
        _currentRequest = null;
        ClearPersistedState();
    }

    private void PersistState()
    {
        string? taskId = _vm.ActiveTaskId;
        if (string.IsNullOrEmpty(taskId) || _remaining <= TimeSpan.Zero || _currentRequest == null)
        {
            ClearPersistedState();
            return;
        }

        Preferences.Default.Set(PreferenceKeys.CurrentTaskId, taskId);
        Preferences.Default.Set(PreferenceKeys.RemainingSeconds, (int)Math.Ceiling(_remaining.TotalSeconds));
        Preferences.Default.Set(PrefTimerMode, (int)_currentRequest.Mode);

        if (_currentRequest.Mode == TimerMode.Pomodoro && _currentRequest.Phase.HasValue)
        {
            Preferences.Default.Set(PrefPomodoroPhase, _currentRequest.Phase.Value == DashboardViewModel.PomodoroPhase.Break ? 1 : 0);
            Preferences.Default.Set(PrefPomodoroCycle, _currentRequest.CycleIndex);
            Preferences.Default.Set(PrefPomodoroTotal, _currentRequest.CycleCount);
            Preferences.Default.Set(PrefPomodoroFocus, _currentRequest.FocusMinutes);
            Preferences.Default.Set(PrefPomodoroBreak, _currentRequest.BreakMinutes);
        }
        else
        {
            Preferences.Default.Remove(PrefPomodoroPhase);
            Preferences.Default.Remove(PrefPomodoroCycle);
            Preferences.Default.Remove(PrefPomodoroTotal);
            Preferences.Default.Remove(PrefPomodoroFocus);
            Preferences.Default.Remove(PrefPomodoroBreak);
        }
    }

    private static void ClearPersistedState()
    {
        Preferences.Default.Remove(PreferenceKeys.CurrentTaskId);
        Preferences.Default.Remove(PreferenceKeys.RemainingSeconds);
        Preferences.Default.Remove(PrefTimerMode);
        Preferences.Default.Remove(PrefPomodoroPhase);
        Preferences.Default.Remove(PrefPomodoroCycle);
        Preferences.Default.Remove(PrefPomodoroTotal);
        Preferences.Default.Remove(PrefPomodoroFocus);
        Preferences.Default.Remove(PrefPomodoroBreak);
    }

    private IDispatcherTimer EnsureTimer()
    {
        if (_timer != null)
        {
            return _timer;
        }

        var timer = Application.Current?.Dispatcher?.CreateTimer() ?? Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += OnTick;
        _timer = timer;
        return timer;
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_remaining <= TimeSpan.Zero)
        {
            _vm.TimerText = DashboardViewModel.FormatTimerText(TimeSpan.Zero);
            _remaining = TimeSpan.Zero;
            if (_timer != null)
            {
                _timer.Stop();
            }

            await _vm.HandleCountdownCompletedAsync();
            return;
        }

        _remaining = _remaining.Subtract(TimeSpan.FromSeconds(1));
        if (_remaining < TimeSpan.Zero)
        {
            _remaining = TimeSpan.Zero;
        }

        _vm.TimerText = DashboardViewModel.FormatTimerText(_remaining);
        PersistState();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        PersistState();
    }
}
