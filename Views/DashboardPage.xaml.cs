using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Storage;
using ShuffleTask.ViewModels;

namespace ShuffleTask.Views;

public partial class DashboardPage : ContentPage
{
    private const string PrefTaskId = "pref.currentTaskId";
    private const string PrefRemainingSecs = "pref.remainingSecs";

    private readonly DashboardViewModel _vm;

    private IDispatcherTimer? _timer;
    private TimeSpan _remaining;

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

        string taskId = Preferences.Default.Get(PrefTaskId, string.Empty);
        int seconds = Preferences.Default.Get(PrefRemainingSecs, -1);

        if (seconds > 0)
        {
            TimeSpan remaining = TimeSpan.FromSeconds(seconds);
            bool restored = await _vm.RestoreTaskAsync(taskId, remaining);
            if (restored)
            {
                _remaining = remaining;
                var timer = EnsureTimer();
                timer.Stop();
                timer.Start();
                return;
            }
        }

        ClearPersistedState();
    }

    private void OnCountdownRequested(object? sender, TimeSpan duration)
    {
        StartCountdown(duration);
    }

    private void OnCountdownCleared(object? sender, EventArgs e)
    {
        StopCountdown();
    }

    private void StartCountdown(TimeSpan duration)
    {
        _remaining = duration;
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
        ClearPersistedState();
    }

    private void PersistState()
    {
        string? taskId = _vm.ActiveTaskId;
        if (string.IsNullOrEmpty(taskId) || _remaining <= TimeSpan.Zero)
        {
            ClearPersistedState();
            return;
        }

        Preferences.Default.Set(PrefTaskId, taskId);
        Preferences.Default.Set(PrefRemainingSecs, (int)Math.Ceiling(_remaining.TotalSeconds));
    }

    private static void ClearPersistedState()
    {
        Preferences.Default.Remove(PrefTaskId);
        Preferences.Default.Remove(PrefRemainingSecs);
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
            StopCountdown();
            await _vm.NotifyTimeUpAsync();
            await _vm.ShuffleAfterTimeoutAsync();
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
