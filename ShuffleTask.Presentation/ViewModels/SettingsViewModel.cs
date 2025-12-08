using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Presentation.Services;
using System.ComponentModel;
using Yaref92.Events.Connections;

namespace ShuffleTask.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IStorageService _storage;
    private readonly ISchedulerService _scheduler;
    private readonly INotificationService _notifications;
    private readonly ShuffleCoordinatorService _coordinator;
    private readonly TimeProvider _clock;
    private readonly INetworkSyncService _networkSync;

    [ObservableProperty]
    private AppSettings _settings;

    [ObservableProperty]
    private bool isBusy;

    public SettingsViewModel(IStorageService storage, ISchedulerService scheduler, INotificationService notifications, ShuffleCoordinatorService coordinator, TimeProvider clock, INetworkSyncService networkSync, AppSettings settings)
    {
        _storage = storage;
        _scheduler = scheduler;
        _notifications = notifications;
        _coordinator = coordinator;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _networkSync = networkSync ?? throw new ArgumentNullException(nameof(networkSync));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool UsePomodoro
    {
        get => Settings.TimerMode == TimerMode.Pomodoro;
        set
        {
            var mode = value ? TimerMode.Pomodoro : TimerMode.LongInterval;
            if (Settings.TimerMode != mode)
            {
                Settings.TimerMode = mode;
                OnPropertyChanged(nameof(UsePomodoro));
            }
        }
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
            var loadedSettings = await _storage.GetSettingsAsync();
            UpdateSettingsFrom(loadedSettings);
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
            ApplyValidation();
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
            DateTimeOffset now = _clock.GetUtcNow();
            var next = _scheduler.PickNextTask(items, Settings, now);
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

    [RelayCommand]
    private async Task ConnectPeerAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            ApplyValidation();
            await _storage.SetSettingsAsync(Settings);
            await _networkSync.ConnectToPeerAsync(Settings.Network.PeerHost, Settings.Network.PeerPort);
        }
        catch (TcpConnectionDisconnectedException ex)
        {
            // Handle connection errors (log, notify user, etc.)
            System.Diagnostics.Debug.WriteLine($"Error connecting to peer: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public string LocalConnectionSummary =>
        Settings?.Network is null
            ? string.Empty
            : $"{Settings.Network.Host}:{Settings.Network.ListeningPort} ({Settings.Network.DeviceId})";

    public string AuthTokenPreview => Settings?.Network?.BuildAuthToken() ?? string.Empty;

    private void ApplyValidation()
    {
        Settings.ReminderMinutes = Math.Clamp(Settings.ReminderMinutes, 1, 480);
        Settings.MinGapMinutes = Math.Clamp(Settings.MinGapMinutes, 0, Settings.MaxGapMinutes);
        Settings.MaxGapMinutes = Math.Clamp(Settings.MaxGapMinutes, Settings.MinGapMinutes, 720);
        Settings.FocusMinutes = Math.Clamp(Settings.FocusMinutes, 1, 240);
        Settings.BreakMinutes = Math.Clamp(Settings.BreakMinutes, 1, 120);
        Settings.PomodoroCycles = Math.Clamp(Settings.PomodoroCycles, 1, 12);
        Settings.Network?.Normalize();
    }

    partial void OnSettingsChanged(AppSettings value)
    {
        OnPropertyChanged(nameof(UsePomodoro));
        OnNetworkChanged(this, new PropertyChangedEventArgs(string.Empty));
    }

    private void OnNetworkChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(LocalConnectionSummary));
        OnPropertyChanged(nameof(AuthTokenPreview));
    }

    private void UpdateSettingsFrom(AppSettings source)
    {
        if (source == null) return;

        // Update all properties from source to the singleton Settings instance
        // This ensures NetworkOptions stays in sync automatically
        Settings.WorkStart = source.WorkStart;
        Settings.WorkEnd = source.WorkEnd;
        Settings.TimerMode = source.TimerMode;
        Settings.FocusMinutes = source.FocusMinutes;
        Settings.BreakMinutes = source.BreakMinutes;
        Settings.PomodoroCycles = source.PomodoroCycles;
        Settings.MinGapMinutes = source.MinGapMinutes;
        Settings.MaxGapMinutes = source.MaxGapMinutes;
        Settings.ReminderMinutes = source.ReminderMinutes;
        Settings.EnableNotifications = source.EnableNotifications;
        Settings.SoundOn = source.SoundOn;
        Settings.Active = source.Active;
        Settings.AutoShuffleEnabled = source.AutoShuffleEnabled;
        Settings.ManualShuffleRespectsAllowedPeriod = source.ManualShuffleRespectsAllowedPeriod;
        Settings.MaxDailyShuffles = source.MaxDailyShuffles;
        Settings.QuietHoursStart = source.QuietHoursStart;
        Settings.QuietHoursEnd = source.QuietHoursEnd;
        Settings.StreakBias = source.StreakBias;
        Settings.StableRandomnessPerDay = source.StableRandomnessPerDay;
        Settings.ImportanceWeight = source.ImportanceWeight;
        Settings.UrgencyWeight = source.UrgencyWeight;
        Settings.UrgencyDeadlineShare = source.UrgencyDeadlineShare;
        Settings.RepeatUrgencyPenalty = source.RepeatUrgencyPenalty;
        Settings.SizeBiasStrength = source.SizeBiasStrength;
        
        // Update NetworkOptions - this automatically updates Settings.Network
        if (source.Network != null)
        {
            Settings.Network = source.Network;
        }
    }
}
