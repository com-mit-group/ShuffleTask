using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShuffleTask.Models;
using ShuffleTask.Services;

namespace ShuffleTask.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly StorageService _storage;
    private readonly SchedulerService _scheduler;
    private readonly NotificationService _notifier;

    public event EventHandler? DoneOccurred;
    public event EventHandler? SkipOccurred;

    public DashboardViewModel(StorageService storage, SchedulerService scheduler, NotificationService notifier)
    {
        _storage = storage;
        _scheduler = scheduler;
        _notifier = notifier;
    }

    [ObservableProperty]
    private TaskItem? _currentTask;

    [ObservableProperty]
    private string _currentTaskDeadlineText = "No deadline";

    partial void OnCurrentTaskChanged(TaskItem? value)
    {
        if (value?.Deadline is DateTime deadline)
        {
            CurrentTaskDeadlineText = $"Deadline / Repeating schedule: {deadline:yyyy-MM-dd HH:mm}";
        }
        else
        {
            CurrentTaskDeadlineText = "No deadline";
        }
    }

    [ObservableProperty]
    private string _countdownText = "60:00";

    [ObservableProperty]
    private bool _isRunning;

    public AppSettings? Settings { get; private set; }

    public async Task InitializeAsync()
    {
        await _storage.InitializeAsync();
        Settings = await _storage.GetSettingsAsync();
        await _notifier.InitializeAsync();
    }

    [RelayCommand]
    public async Task<int> Shuffle()
    {

        List<TaskItem> tasks = await _storage.GetTasksAsync();
        Settings ??= await _storage.GetSettingsAsync();
        TaskItem? picked = _scheduler.PickNextTask(tasks, Settings, DateTime.Now);
        CurrentTask = picked;
        int minutes = Settings.ReminderMinutes > 0 ? Settings.ReminderMinutes : 60;
        return minutes;
    }

    public async Task NotifyCurrentTaskAsync(int minutes)
    {
        Settings ??= await _storage.GetSettingsAsync();
        if (Settings.EnableNotifications && CurrentTask != null)
        {
            await _notifier.NotifyTaskAsync(CurrentTask, minutes, Settings);
        }
    }

    public async Task TimeUpAsync()
    {
        Settings ??= await _storage.GetSettingsAsync();
        await _notifier.ShowToastAsync("Time's up", "Shuffling a new task...", Settings);
    }

    [RelayCommand]
    public async Task Done()
    {
        if (CurrentTask == null) return;
        await _storage.MarkTaskDoneAsync(CurrentTask.Id);
        DoneOccurred?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public Task Snooze()
    {
        // Snooze is handled via notifications UI; here we can treat as Skip for flow
        SkipOccurred?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}
