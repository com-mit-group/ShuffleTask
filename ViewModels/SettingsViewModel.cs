using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShuffleTask.Models;
using ShuffleTask.Services;

namespace ShuffleTask.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly StorageService _storage;
    private readonly SchedulerService _scheduler;
    private readonly NotificationService _notifications;
    private readonly ShuffleCoordinatorService _coordinator;

    [ObservableProperty]
    private AppSettings settings = new();

    [ObservableProperty]
    private bool isBusy;

    public SettingsViewModel(StorageService storage, SchedulerService scheduler, NotificationService notifications, ShuffleCoordinatorService coordinator)
    {
        _storage = storage;
        _scheduler = scheduler;
        _notifications = notifications;
        _coordinator = coordinator;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _storage.InitializeAsync();
            Settings = await _storage.GetSettingsAsync();
            Settings.NormalizeWeights();
            await _notifications.InitializeAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _storage.SetSettingsAsync(Settings);
            await _coordinator.RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ShufflePreviewAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _storage.InitializeAsync();
            var items = await _storage.GetTasksAsync();
            var next = _scheduler.PickNextTask(items, Settings, DateTime.Now);
            if (next != null && Settings.EnableNotifications)
            {
                await _notifications.NotifyTaskAsync(next, Settings.ReminderMinutes, Settings);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
