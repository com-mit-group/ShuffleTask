using System.Globalization;
using MauiApplication = Microsoft.Maui.Controls.Application;
using ShuffleTask.Application.Models;
using ShuffleTask.Presentation.Utilities;
using ShuffleTask.ViewModels;

namespace ShuffleTask.Views;

public partial class DashboardPage : ContentPage
{
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

        var mode = (TimerMode)Preferences.Default.Get(PreferenceKeys.TimerMode, (int)TimerMode.LongInterval);

        if (PersistedTimerState.TryGetActiveTimer(
                out string taskId,
                out TimeSpan remaining,
                out bool expired,
                out int durationSeconds,
                out _))
        {
            DashboardViewModel.TimerRequest? timerState = null;

            if (mode == TimerMode.Pomodoro)
            {
                int phaseValue = Preferences.Default.Get(PreferenceKeys.PomodoroPhase, 0);
                int cycle = Math.Max(1, Preferences.Default.Get(PreferenceKeys.PomodoroCycle, 1));
                int total = Math.Max(1, Preferences.Default.Get(PreferenceKeys.PomodoroTotal, 1));
                int focus = Math.Max(1, Preferences.Default.Get(PreferenceKeys.PomodoroFocus, 15));
                int breakMinutes = Math.Max(1, Preferences.Default.Get(PreferenceKeys.PomodoroBreak, 5));

                var phase = (DashboardViewModel.PomodoroPhase)Math.Clamp(phaseValue, 0, 1);
                TimeSpan duration = phase == DashboardViewModel.PomodoroPhase.Break
                    ? TimeSpan.FromMinutes(breakMinutes)
                    : TimeSpan.FromMinutes(focus);

                timerState = DashboardViewModel.TimerRequest.Pomodoro(
                    duration,
                    phase,
                    cycle,
                    total,
                    focus,
                    breakMinutes);
            }
            else
            {
                timerState = DashboardViewModel.TimerRequest.LongInterval(
                    TimeSpan.FromSeconds(Math.Max(1, durationSeconds)));
            }

            bool restored = await _vm.RestoreTaskAsync(taskId, remaining, timerState);
            if (restored)
            {
                _currentRequest = timerState;
                if (expired)
                {
                    _remaining = TimeSpan.Zero;
                    ClearPersistedState();
                    await _vm.HandleCountdownCompletedAsync();
                    return;
                }

                _remaining = remaining;
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
        Preferences.Default.Set(
            PreferenceKeys.TimerDurationSeconds,
            Math.Max(1, (int)Math.Ceiling(_currentRequest.Duration.TotalSeconds)));
        Preferences.Default.Set(
            PreferenceKeys.TimerExpiresAt,
            DateTimeOffset.UtcNow.Add(_remaining).ToString("O", CultureInfo.InvariantCulture));
        Preferences.Default.Set(PreferenceKeys.TimerMode, (int)_currentRequest.Mode);

        if (_currentRequest.Mode == TimerMode.Pomodoro && _currentRequest.Phase.HasValue)
        {
            Preferences.Default.Set(PreferenceKeys.PomodoroPhase, _currentRequest.Phase.Value == DashboardViewModel.PomodoroPhase.Break ? 1 : 0);
            Preferences.Default.Set(PreferenceKeys.PomodoroCycle, _currentRequest.CycleIndex);
            Preferences.Default.Set(PreferenceKeys.PomodoroTotal, _currentRequest.CycleCount);
            Preferences.Default.Set(PreferenceKeys.PomodoroFocus, _currentRequest.FocusMinutes);
            Preferences.Default.Set(PreferenceKeys.PomodoroBreak, _currentRequest.BreakMinutes);
        }
        else
        {
            Preferences.Default.Remove(PreferenceKeys.PomodoroPhase);
            Preferences.Default.Remove(PreferenceKeys.PomodoroCycle);
            Preferences.Default.Remove(PreferenceKeys.PomodoroTotal);
            Preferences.Default.Remove(PreferenceKeys.PomodoroFocus);
            Preferences.Default.Remove(PreferenceKeys.PomodoroBreak);
        }
    }

    private static void ClearPersistedState()
    {
        PersistedTimerState.Clear();
        Preferences.Default.Remove(PreferenceKeys.TimerMode);
        Preferences.Default.Remove(PreferenceKeys.PomodoroPhase);
        Preferences.Default.Remove(PreferenceKeys.PomodoroCycle);
        Preferences.Default.Remove(PreferenceKeys.PomodoroTotal);
        Preferences.Default.Remove(PreferenceKeys.PomodoroFocus);
        Preferences.Default.Remove(PreferenceKeys.PomodoroBreak);
    }

    private IDispatcherTimer EnsureTimer()
    {
        if (_timer != null)
        {
            return _timer;
        }

        var timer = MauiApplication.Current?.Dispatcher?.CreateTimer() ?? Dispatcher.CreateTimer();
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
