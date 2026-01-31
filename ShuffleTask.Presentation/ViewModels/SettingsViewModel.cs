using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Exceptions;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Presentation.Services;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Yaref92.Events.Transport.Grpc;

namespace ShuffleTask.ViewModels;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IStorageService _storage;
    private readonly ISchedulerService _scheduler;
    private readonly INotificationService _notifications;
    private readonly ShuffleCoordinatorService _coordinator;
    private readonly TimeProvider _clock;
    private readonly INetworkSyncService _networkSync;
    private readonly TasksViewModel _tasksViewModel;
    private readonly ILogger<SettingsViewModel>? _logger;
    private const int MaxUsernameLength = 64;
    private const string Windows = "Windows";
    private NetworkOptions? _networkOptions;
    private string? _lastUserId;
    private bool _lastAnonymousMode;
    private bool _disposed;
    private bool _lastBackgroundActivityEnabled;
    private readonly SemaphoreSlim _backgroundActivityToggleGate = new(1, 1);
    private PeriodDefinition? _morningDefinition;
    private PeriodDefinition? _eveningDefinition;
    private PeriodDefinition? _lunchDefinition;

    private string _selectedPeerPlatform = Windows;

    [ObservableProperty]
    private AppSettings _settings;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private TimeSpan morningStart = new(7, 0, 0);

    [ObservableProperty]
    private TimeSpan morningEnd = new(10, 0, 0);

    [ObservableProperty]
    private TimeSpan eveningStart = new(18, 0, 0);

    [ObservableProperty]
    private TimeSpan eveningEnd = new(21, 0, 0);

    [ObservableProperty]
    private TimeSpan lunchStart = new(12, 0, 0);

    [ObservableProperty]
    private TimeSpan lunchEnd = new(13, 0, 0);

    public IReadOnlyList<string> PeerPlatforms { get; } = new[] { "Android", Windows };

    public string SelectedPeerPlatform
    {
        get => _selectedPeerPlatform;
        set => SetProperty(ref _selectedPeerPlatform, value);
    }

    public SettingsViewModel(IStorageService storage, ISchedulerService scheduler, INotificationService notifications,
                             ShuffleCoordinatorService coordinator, TimeProvider clock, INetworkSyncService networkSync,
                             AppSettings settings, TasksViewModel tasksViewModel, ILogger<SettingsViewModel>? logger = null)
    {
        _storage = storage;
        _scheduler = scheduler;
        _notifications = notifications;
        _coordinator = coordinator;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _networkSync = networkSync ?? throw new ArgumentNullException(nameof(networkSync));
        _tasksViewModel = tasksViewModel ?? throw new ArgumentNullException(nameof(tasksViewModel));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
        UpdateNetworkSubscription(null, settings.Network);
        SubscribeSettings(settings);
        CacheSessionState();
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
    private Task LoadAsync()
    {
        return ExecuteIfNotBusyAsync(async () =>
        {
            await _storage.InitializeAsync();
            Settings.NormalizeWeights();
            Settings.Network?.Normalize();
            await _notifications.InitializeAsync();
            await LoadPresetDefinitionsAsync();
            CacheSessionState();
        });
    }

    [RelayCommand]
    private Task SaveAsync()
    {
        return ExecuteIfNotBusyAsync(async () =>
        {
            bool wasAnonymous = _lastAnonymousMode;
            ApplyValidation();
            SyncSlotSettingsFromViewModel();
            await PersistSettingsAsync();
            await PersistPresetDefinitionsAsync();
            bool sessionChanged = wasAnonymous != IsAnonymousSession || !string.Equals(_lastUserId, Settings.Network.UserId, StringComparison.Ordinal);
            if (sessionChanged)
            {
                await HandleSessionTransitionAsync(wasAnonymous);
            }
            else
            {
                await _coordinator.RefreshAsync();
            }

            CacheSessionState();
        });
    }

    [RelayCommand]
    private Task ShufflePreviewAsync()
    {
        return ExecuteIfNotBusyAsync(async () =>
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
        });
    }

    [RelayCommand]
    private Task ConnectPeerAsync()
    {
        return ExecuteIfNotBusyAsync(async () =>
        {
            if (!CanSyncAcrossDevices)
            {
                await ShowLoginRequiredAlertAsync();
                return;
            }

            try
            {
                ApplyValidation();
                await PersistSettingsAsync();
                await _networkSync.ConnectToPeerAsync(Settings.Network.PeerHost, Settings.Network.PeerPort, SelectedPeerPlatform);
            }
            catch (InvalidOperationException)
            {
                await ShowLoginRequiredAlertAsync();
            }
            catch (NetworkConnectionException ex)
            {
                await ShowConnectionErrorAsync(ex.Message);
            }
            catch (Exception ex)
            {
                await ShowConnectionErrorAsync(ex.Message);
            }
        });
    }

    [RelayCommand]
    private Task LoginAsync(string? username)
    {
        if (Settings?.Network is null)
        {
            return Task.CompletedTask;
        }

        return ExecuteIfNotBusyAsync(async () =>
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
            await PersistSettingsAsync();
            OnNetworkChanged(this, new PropertyChangedEventArgs(nameof(Settings.Network.UserId)));
            await HandleSessionTransitionAsync(wasAnonymous);

            CacheSessionState();
        });
    }

    [RelayCommand]
    private Task LogoutAsync()
    {
        if (Settings?.Network == null)
        {
            return Task.CompletedTask;
        }

        return ExecuteIfNotBusyAsync(async () =>
        {
            bool wasAnonymous = _lastAnonymousMode;
            await _networkSync.RequestGracefulFlushAsync();
            await _networkSync.DisconnectAsync();
            Settings.Network.AnonymousSession = true;
            Settings.Network.UserId = null;
            await PersistSettingsAsync();
            OnNetworkChanged(this, new PropertyChangedEventArgs(string.Empty));
            await HandleSessionTransitionAsync(wasAnonymous);

            CacheSessionState();
        });
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

    public string UserDisplayLabel => string.IsNullOrWhiteSpace(Settings?.Network?.UserId)
        ? "Not logged in"
        : Settings.Network.UserId;

    private Task ExecuteIfNotBusyAsync(Func<Task> operation)
    {
        if (IsBusy)
        {
            return Task.CompletedTask;
        }

        return RunBusyGuardAsync(this, operation);
    }

    private static async Task RunBusyGuardAsync(SettingsViewModel viewModel, Func<Task> operation)
    {
        viewModel.IsBusy = true;
        try
        {
            await operation();
        }
        finally
        {
            viewModel.IsBusy = false;
        }
    }

    private void CacheSessionState()
    {
        _lastUserId = Settings.Network?.UserId;
        _lastAnonymousMode = IsAnonymousSession;
    }

    private async Task LoadPresetDefinitionsAsync()
    {
        _morningDefinition = await _storage.GetPeriodDefinitionAsync(PeriodDefinitionCatalog.MorningsId);
        _eveningDefinition = await _storage.GetPeriodDefinitionAsync(PeriodDefinitionCatalog.EveningsId);
        _lunchDefinition = await _storage.GetPeriodDefinitionAsync(PeriodDefinitionCatalog.LunchBreakId);

        ApplyDefinitionTimes(_morningDefinition, PeriodDefinitionCatalog.Mornings, start => MorningStart = start, end => MorningEnd = end);
        ApplyDefinitionTimes(_eveningDefinition, PeriodDefinitionCatalog.Evenings, start => EveningStart = start, end => EveningEnd = end);
        ApplyDefinitionTimes(_lunchDefinition, PeriodDefinitionCatalog.LunchBreak, start => LunchStart = start, end => LunchEnd = end);
        SyncSlotSettingsFromViewModel();
    }

    private async Task PersistPresetDefinitionsAsync()
    {
        await UpsertPresetDefinitionAsync(_morningDefinition, PeriodDefinitionCatalog.Mornings, MorningStart, MorningEnd);
        await UpsertPresetDefinitionAsync(_eveningDefinition, PeriodDefinitionCatalog.Evenings, EveningStart, EveningEnd);
        await UpsertPresetDefinitionAsync(_lunchDefinition, PeriodDefinitionCatalog.LunchBreak, LunchStart, LunchEnd);
    }

    private static void ApplyDefinitionTimes(PeriodDefinition? definition, PeriodDefinition fallback, Action<TimeSpan> setStart, Action<TimeSpan> setEnd)
    {
        TimeSpan start = definition?.StartTime ?? fallback.StartTime ?? TimeSpan.Zero;
        TimeSpan end = definition?.EndTime ?? fallback.EndTime ?? TimeSpan.Zero;
        setStart(start);
        setEnd(end);
    }

    private async Task UpsertPresetDefinitionAsync(PeriodDefinition? definition, PeriodDefinition fallback, TimeSpan start, TimeSpan end)
    {
        definition ??= PeriodDefinitionCatalog.CreatePresetDefinitions()
            .FirstOrDefault(item => string.Equals(item.Id, fallback.Id, StringComparison.OrdinalIgnoreCase))
            ?? fallback;

        definition.StartTime = start;
        definition.EndTime = end;
        definition.IsAllDay = false;
        definition.Mode = fallback.Mode;

        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            definition.Id = fallback.Id;
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            definition.Name = fallback.Name;
        }

        await _storage.UpdatePeriodDefinitionAsync(definition);
    }

    private void SyncSlotSettingsFromViewModel()
    {
        Settings.MorningStart = MorningStart;
        Settings.MorningEnd = MorningEnd;
        Settings.LunchStart = LunchStart;
        Settings.LunchEnd = LunchEnd;
        Settings.EveningStart = EveningStart;
        Settings.EveningEnd = EveningEnd;
    }

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
        SubscribeSettings(value);
        OnPropertyChanged(nameof(UsePomodoro));
        OnNetworkChanged(this, new PropertyChangedEventArgs(string.Empty));
    }

    partial void OnSettingsChanging(AppSettings value)
    {
        _ = value;
        UnsubscribeSettings(_settings);
        UpdateNetworkSubscription(_networkOptions, null);
    }

    private void OnNetworkChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(LocalConnectionSummary));
        OnPropertyChanged(nameof(AuthTokenPreview));
        OnPropertyChanged(nameof(IsAnonymousSession));
        OnPropertyChanged(nameof(CanSyncAcrossDevices));
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(UserDisplayLabel));
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

    private void SubscribeSettings(AppSettings settings)
    {
        _lastBackgroundActivityEnabled = settings.BackgroundActivityEnabled;
        settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void UnsubscribeSettings(AppSettings? settings)
    {
        if (settings is null)
        {
            return;
        }

        settings.PropertyChanged -= OnSettingsPropertyChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppSettings.BackgroundActivityEnabled))
        {
            return;
        }

        bool enabled = Settings.BackgroundActivityEnabled;
        if (enabled == _lastBackgroundActivityEnabled)
        {
            return;
        }

        _lastBackgroundActivityEnabled = enabled;
        _logger?.LogInformation("Background activity toggled {State}.", enabled ? "on" : "off");
        _ = HandleBackgroundActivityToggleAsync(enabled);
    }

    private async Task HandleBackgroundActivityToggleAsync(bool enabled)
    {
        await _backgroundActivityToggleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await PersistSettingsAsync().ConfigureAwait(false);
            await _coordinator.ApplyBackgroundActivityChangeAsync(enabled).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to apply background activity toggle.");
        }
        finally
        {
            _backgroundActivityToggleGate.Release();
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

    private static Task<bool> PromptMigrateDeviceTasksAsync()
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
                Microsoft.Maui.Controls.Application.Current?.MainPage?.DisplayPromptAsync(
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
        if (wasAnonymous && !IsAnonymousSession && !string.IsNullOrWhiteSpace(Settings.Network.UserId)
            && ShouldPromptForMigration())
        {
            bool migrate = await SettingsViewModel.PromptMigrateDeviceTasksAsync();
            if (migrate)
            {
                await _storage.MigrateDeviceTasksToUserAsync(Settings.Network.DeviceId, Settings.Network.UserId);
            }
        }

        var (userScope, deviceScope) = ResolveTaskScopes();

        await _coordinator.RefreshAsync();
        await RefreshBoundCollectionsAsync(userScope, deviceScope);
    }

    private bool ShouldPromptForMigration()
    {
        return _tasksViewModel.Tasks.Count > 0;
    }

    private (string? UserId, string DeviceId) ResolveTaskScopes()
    {
        bool anonymous = IsAnonymousSession;
        string? userScope = anonymous ? null : Settings.Network?.UserId;
        string deviceScope = anonymous ? Settings.Network?.DeviceId ?? string.Empty : string.Empty;

        return (userScope, deviceScope);
    }

    private Task RefreshBoundCollectionsAsync(string? userScope, string? deviceScope)
    {
        if (_tasksViewModel is null)
        {
            return Task.CompletedTask;
        }

        return MainThread.InvokeOnMainThreadAsync(() => _tasksViewModel.LoadAsync(userScope, deviceScope));
    }

    private static Task ShowLoginRequiredAlertAsync()
    {
        const string title = "Sync unavailable";
        const string message = "Log in to sync";

        return MainThread.InvokeOnMainThreadAsync(() =>
            Microsoft.Maui.Controls.Application.Current?.MainPage?.DisplayAlert(title, message, "OK")
            ?? Task.CompletedTask);
    }

    private async Task PersistSettingsAsync(bool broadcast = true)
    {
        Settings.Touch(_clock);
        await _storage.SetSettingsAsync(Settings);

        if (broadcast)
        {
            await _networkSync.PublishSettingsUpdatedAsync(Settings);
        }
    }

    private async Task ShowConnectionErrorAsync(string message)
    {
        const string title = "Peer connection failed";

        try
        {
            Task toast = _notifications.ShowToastAsync(title, message, Settings);
            Task alert = MainThread.InvokeOnMainThreadAsync(() =>
                Microsoft.Maui.Controls.Application.Current?.MainPage?.DisplayAlert(title, message, "OK") ?? Task.CompletedTask);

            await Task.WhenAll(toast, alert);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error showing connection error toast: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UpdateNetworkSubscription(_networkOptions, null);
        UnsubscribeSettings(Settings);
        _backgroundActivityToggleGate.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

}
