using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
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
    private readonly TasksViewModel _tasksViewModel;
    private const int MaxUsernameLength = 64;
    private NetworkOptions? _networkOptions;
    private string? _lastUserId;
    private bool _lastAnonymousMode;

    [ObservableProperty]
    private AppSettings _settings;

    [ObservableProperty]
    private bool isBusy;

    public SettingsViewModel(IStorageService storage, ISchedulerService scheduler, INotificationService notifications, ShuffleCoordinatorService coordinator, TimeProvider clock, INetworkSyncService networkSync, AppSettings settings, TasksViewModel tasksViewModel)
    {
        _storage = storage;
        _scheduler = scheduler;
        _notifications = notifications;
        _coordinator = coordinator;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _networkSync = networkSync ?? throw new ArgumentNullException(nameof(networkSync));
        _tasksViewModel = tasksViewModel ?? throw new ArgumentNullException(nameof(tasksViewModel));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        UpdateNetworkSubscription(null, settings.Network);
        _lastUserId = settings.Network?.UserId;
        _lastAnonymousMode = IsAnonymousSession;
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
            Settings.NormalizeWeights();
            Settings.Network?.Normalize();
            await _notifications.InitializeAsync();
            _lastUserId = Settings.Network?.UserId;
            _lastAnonymousMode = IsAnonymousSession;
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
            bool wasAnonymous = _lastAnonymousMode;
            ApplyValidation();
            await _storage.SetSettingsAsync(Settings);
            bool sessionChanged = wasAnonymous != IsAnonymousSession || !string.Equals(_lastUserId, Settings.Network.UserId, StringComparison.Ordinal);
            if (sessionChanged)
            {
                await HandleSessionTransitionAsync(wasAnonymous);
            }
            else
            {
                await _coordinator.RefreshAsync();
            }

            _lastUserId = Settings.Network.UserId;
            _lastAnonymousMode = IsAnonymousSession;
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
            var network = Settings.Network;
            var items = await _storage.GetTasksAsync(network?.UserId, network?.DeviceId ?? string.Empty);
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

    [RelayCommand]
    private async Task LoginAsync(string? username)
    {
        if (IsBusy)
        {
            return;
        }

        if (Settings?.Network is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            string? trimmedUsername = await GetUsernameAsync(username);
            if (string.IsNullOrWhiteSpace(trimmedUsername))
            {
                return;
            }

            bool wasAnonymous = IsAnonymousSession;
            Settings.Network.UserId = trimmedUsername;
            Settings.Network.AnonymousSession = false;
            ApplyValidation();
            await _storage.SetSettingsAsync(Settings);
            OnNetworkChanged(this, new PropertyChangedEventArgs(nameof(Settings.Network.UserId)));
            await HandleSessionTransitionAsync(wasAnonymous);

            _lastUserId = Settings.Network.UserId;
            _lastAnonymousMode = IsAnonymousSession;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        if (IsBusy || Settings?.Network == null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            bool wasAnonymous = _lastAnonymousMode;
            await _networkSync.DisconnectAsync();
            Settings.Network.AnonymousSession = true;
            Settings.Network.UserId = null;
            OnNetworkChanged(this, new PropertyChangedEventArgs(string.Empty));
            await _storage.SetSettingsAsync(Settings);
            await HandleSessionTransitionAsync(wasAnonymous);

            _lastUserId = Settings.Network.UserId;
            _lastAnonymousMode = IsAnonymousSession;
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

    public bool IsAnonymousSession
    {
        get => DeriveIsAnonymousSession(Settings?.Network);
    }

    public bool CanSyncAcrossDevices => !IsAnonymousSession && !string.IsNullOrWhiteSpace(Settings?.Network?.UserId);

    public bool IsLoggedIn => !IsAnonymousSession;

    private void ApplyValidation()
    {
        Settings.ReminderMinutes = Math.Clamp(Settings.ReminderMinutes, 1, 480);
        Settings.MinGapMinutes = Math.Clamp(Settings.MinGapMinutes, 0, Settings.MaxGapMinutes);
        Settings.MaxGapMinutes = Math.Clamp(Settings.MaxGapMinutes, Settings.MinGapMinutes, 720);
        Settings.FocusMinutes = Math.Clamp(Settings.FocusMinutes, 1, 240);
        Settings.BreakMinutes = Math.Clamp(Settings.BreakMinutes, 1, 120);
        Settings.PomodoroCycles = Math.Clamp(Settings.PomodoroCycles, 1, 12);
        Settings.Network?.Normalize();
        OnPropertyChanged(nameof(IsAnonymousSession));
        OnPropertyChanged(nameof(CanSyncAcrossDevices));
        OnPropertyChanged(nameof(IsLoggedIn));
    }

    partial void OnSettingsChanged(AppSettings value)
    {
        UpdateNetworkSubscription(_networkOptions, value.Network);
        OnPropertyChanged(nameof(UsePomodoro));
        OnNetworkChanged(this, new PropertyChangedEventArgs(string.Empty));
    }

    partial void OnSettingsChanging(AppSettings value)
    {
        _ = value;
        UpdateNetworkSubscription(_networkOptions, null);
    }

    private void OnNetworkChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(LocalConnectionSummary));
        OnPropertyChanged(nameof(AuthTokenPreview));
        OnPropertyChanged(nameof(IsAnonymousSession));
        OnPropertyChanged(nameof(CanSyncAcrossDevices));
        OnPropertyChanged(nameof(IsLoggedIn));
    }

    private void UpdateNetworkSubscription(NetworkOptions? oldNetwork, NetworkOptions? newNetwork)
    {
        if (oldNetwork != null)
        {
            oldNetwork.PropertyChanged -= OnNetworkChanged;
        }

        _networkOptions = newNetwork;

        if (newNetwork != null)
        {
            newNetwork.PropertyChanged += OnNetworkChanged;
        }
    }

    private static bool DeriveIsAnonymousSession(NetworkOptions? network)
    {
        if (network is null)
        {
            return true;
        }

        return network.AnonymousSession || string.IsNullOrWhiteSpace(network.UserId);
    }

    private Task<bool> PromptMigrateDeviceTasksAsync()
    {
        const string title = "Sync device tasks?";
        const string message = "You just signed in. Do you want to attach tasks from this device to your account for cross-device sync?";
        return MainThread.InvokeOnMainThreadAsync(() =>
            Microsoft.Maui.Controls.Application.Current?.MainPage?.DisplayAlert(title, message, "Yes", "No") ?? Task.FromResult(false));
    }

    private async Task<string?> GetUsernameAsync(string? username)
    {
        string? candidate = username;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = await MainThread.InvokeOnMainThreadAsync(() =>
                Application.Current?.MainPage?.DisplayPromptAsync(
                    "Login",
                    "Enter your username",
                    "OK",
                    "Cancel",
                    initialValue: _lastUserId ?? string.Empty,
                    maxLength: MaxUsernameLength) ?? Task.FromResult<string?>(null));
        }

        candidate = candidate?.Trim();

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (candidate.Length > MaxUsernameLength)
        {
            candidate = candidate[..MaxUsernameLength];
        }

        return candidate;
    }

    private async Task HandleSessionTransitionAsync(bool wasAnonymous)
    {
        if (wasAnonymous && !IsAnonymousSession && !string.IsNullOrWhiteSpace(Settings.Network.UserId))
        {
            bool migrate = await PromptMigrateDeviceTasksAsync();
            if (migrate)
            {
                await _storage.MigrateDeviceTasksToUserAsync(Settings.Network.DeviceId, Settings.Network.UserId);
            }
        }

        await _coordinator.RefreshAsync();
        await RefreshBoundCollectionsAsync();
    }

    private Task RefreshBoundCollectionsAsync()
    {
        if (_tasksViewModel is null)
        {
            return Task.CompletedTask;
        }

        return MainThread.InvokeOnMainThreadAsync(_tasksViewModel.LoadAsync);
    }

}
