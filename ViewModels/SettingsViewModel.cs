using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShuffleTask.Models;
using ShuffleTask.Services;

namespace ShuffleTask.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly StorageService _storage;
    private readonly SchedulerService _scheduler;
    private readonly NotificationService _notifier;

    public SettingsViewModel(StorageService storage, SchedulerService scheduler, NotificationService notifier)
    {
        _storage = storage;
        _scheduler = scheduler;
        _notifier = notifier;
    }

    [ObservableProperty]
    private AppSettings settings = new();

    [RelayCommand]
    public async Task LoadAsync()
    {
        await _storage.InitializeAsync();
        Settings = await _storage.GetSettingsAsync();
        await _notifier.InitializeAsync();
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        await _storage.SetSettingsAsync(Settings);
    }

    [RelayCommand]
    public async Task ShuffleNowAsync()
    {
        // Guard: load settings and tasks
        await _storage.InitializeAsync();
        var currentSettings = await _storage.GetSettingsAsync();
        var tasks = await _storage.GetTasksAsync();
        var t = _scheduler.PickNextTask(tasks, currentSettings, DateTime.Now);
        if (t != null)
        {
            if (currentSettings.EnableNotifications)
            {
                await _notifier.NotifyTaskAsync(t, currentSettings.ReminderMinutes, currentSettings);
            }
        }
    }
}
