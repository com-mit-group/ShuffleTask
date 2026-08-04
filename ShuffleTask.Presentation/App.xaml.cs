using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Presentation;
using ShuffleTask.Presentation.Models;
using ShuffleTask.Presentation.Services;
using ShuffleTask.Presentation.Utilities;
using ShuffleTask.Views;

namespace ShuffleTask;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly IStorageService _storage;
    private readonly ShuffleCoordinatorService _coordinator;
    private readonly TimeProvider _clock;
    private readonly AppSettings _settings;
    private readonly IShuffleLogger? _logger;
    private readonly MainPage _mainPage;
    private bool _startupRunning;
    private bool _resumeRunning;

    public OperationState StartupOperationState { get; } = new();

    public App(MainPage mainPage, IStorageService storage, ShuffleCoordinatorService coordinator, TimeProvider clock, AppSettings settings, IShuffleLogger? logger = null)
    {
        InitializeComponent();
        MainPage = mainPage;
        _storage = storage;
        _coordinator = coordinator;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
        _mainPage = mainPage;
        StartupOperationState.PropertyChanged += OnStartupOperationStateChanged;
        RequestedThemeChanged += (_, __) => { };
    }

    private void OnStartupOperationStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OperationState.Announcement)
            || string.IsNullOrWhiteSpace(StartupOperationState.Announcement))
        {
            return;
        }

        OperationStateAccessibility.Announce(Dispatcher, null, StartupOperationState);
    }

    protected override async void OnStart()
    {
        base.OnStart();
        await StartAsync();
    }

    internal async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_startupRunning)
        {
            return;
        }

        _startupRunning = true;
        StartupOperationState.SetLoading("Starting ShuffleTask…");
        try
        {
            await _mainPage.ShowOnboardingLoadingAsync();
            cancellationToken.ThrowIfCancellationRequested();
            await _storage.InitializeAsync();
            await PersistedTimerState.RecoverAgainstStorageAsync(_storage, _logger);
            if (_settings.BackgroundActivityEnabled)
            {
                await _coordinator.StartAsync();
            }

            StartupOperationState.SetSuccess("ShuffleTask is ready.");
            await _mainPage.ResolveOnboardingAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StartupOperationState.SetTransientFailure(
                "Startup was canceled. Retry when you are ready.",
                null,
                StartAsync,
                isBlocking: true);
            await _mainPage.ShowOnboardingFailureAsync(StartAsync);
        }
        catch (Exception ex)
        {
            _logger?.LogOperation(LogLevel.Critical, "ApplicationStartup", "Application startup failed.", ex);
            StartupOperationState.SetFatalFailure(
                "ShuffleTask could not start safely. Check local storage access and retry.",
                null,
                StartAsync);
            await _mainPage.ShowOnboardingFailureAsync(StartAsync);
        }
        finally
        {
            _startupRunning = false;
        }
    }

    protected override async void OnResume()
    {
        base.OnResume();
        await ResumeAsync();
    }

    internal async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (_resumeRunning || !_settings.BackgroundActivityEnabled)
        {
            return;
        }

        _resumeRunning = true;
        StartupOperationState.SetLoading("Restoring ShuffleTask…");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await PersistedTimerState.RecoverAgainstStorageAsync(_storage, _logger);
            await _coordinator.ResumeAsync();
            StartupOperationState.SetSuccess("ShuffleTask restored.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StartupOperationState.SetTransientFailure(
                "Restore was canceled. Retry when you are ready.",
                null,
                ResumeAsync);
        }
        catch (Exception ex)
        {
            _logger?.LogOperation(LogLevel.Error, "ApplicationResume", "Application resume failed.", ex);
            StartupOperationState.SetTransientFailure(
                "ShuffleTask could not restore background activity. Your saved tasks are unchanged.",
                null,
                ResumeAsync);
        }
        finally
        {
            _resumeRunning = false;
        }
    }

    protected override void OnSleep()
    {
        base.OnSleep();
        _coordinator.SuspendInProcessTimer();
    }

}
