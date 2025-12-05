using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Presentation.Services;
using System.ComponentModel;

namespace ShuffleTask.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IStorageService _storage;
    private readonly ISchedulerService _scheduler;
    private readonly INotificationService _notifications;
    private readonly ShuffleCoordinatorService _coordinator;
    private readonly TimeProvider _clock;
    private readonly INetworkSyncService _networkSync;
    private NetworkOptions? _currentNetworkOptions;

    [ObservableProperty]
    private AppSettings settings = new();

    [ObservableProperty]
    private bool isBusy;

    public SettingsViewModel(IStorageService storage, ISchedulerService scheduler, INotificationService notifications, ShuffleCoordinatorService coordinator, TimeProvider clock, INetworkSyncService networkSync)
    {
        _storage = storage;
        _scheduler = scheduler;
        _notifications = notifications;
        _coordinator = coordinator;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _networkSync = networkSync ?? throw new ArgumentNullException(nameof(networkSync));
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
            Settings = await _storage.GetSettingsAsync();
            Settings.NormalizeWeights();
            await _notifications.InitializeAsync();
            TrackNetworkOptions(Settings.Network);
            await _networkSync.ApplyOptionsAsync(Settings.Network);
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
            await _networkSync.ApplyOptionsAsync(Settings.Network);
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
            await _networkSync.ApplyOptionsAsync(Settings.Network);
            await _networkSync.ConnectToPeerAsync(Settings.Network.PeerHost, Settings.Network.PeerPort);
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
        TrackNetworkOptions(value.Network);
    }

    private void TrackNetworkOptions(NetworkOptions? options)
    {
        if (_currentNetworkOptions is not null)
        {
            _currentNetworkOptions.PropertyChanged -= OnNetworkChanged;
        }

        if (options is null)
        {
            return;
        }

        _currentNetworkOptions = options;
        _currentNetworkOptions.PropertyChanged += OnNetworkChanged;
        OnNetworkChanged(this, new PropertyChangedEventArgs(string.Empty));
    }

    private void OnNetworkChanged(object? sender, PropertyChangedEventArgs e)
    {
        Settings.Network?.EnsureListeningPort();
        OnPropertyChanged(nameof(LocalConnectionSummary));
        OnPropertyChanged(nameof(AuthTokenPreview));
    }
}
