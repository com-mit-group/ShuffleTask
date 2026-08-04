using Microsoft.Extensions.Logging;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Presentation;
using ShuffleTask.Presentation.Services;
using ShuffleTask.ViewModels;
using MauiApplication = Microsoft.Maui.Controls.Application;

namespace ShuffleTask.Views;

public partial class MainPage : TabbedPage
{
    private bool _tabsInitialized;
    private DashboardViewModel? _dashboardViewModel;
    private TasksViewModel? _tasksViewModel;
    private TasksPage? _tasksPage;
    private OnboardingPage? _onboardingPage;
    private OnboardingViewModel? _onboardingViewModel;
    private NavigationPage? _dashboardTab;
    private NavigationPage? _tasksTab;
    private NavigationPage? _onboardingModal;

    public MainPage()
    {
        InitializeComponent();
        TryInitializeFromServices();
    }

    public MainPage(
        DashboardPage dashboardPage,
        TasksPage tasksPage,
        PeersPage peersPage,
        SettingsPage settingsPage,
        OnboardingPage onboardingPage,
        DashboardViewModel dashboardViewModel,
        TasksViewModel tasksViewModel,
        OnboardingViewModel onboardingViewModel)
    {
        InitializeComponent();
        ConfigureTabs(
            dashboardPage,
            tasksPage,
            peersPage,
            settingsPage,
            onboardingPage,
            dashboardViewModel,
            tasksViewModel,
            onboardingViewModel);
    }

    private void TryInitializeFromServices()
    {
        if (_tabsInitialized)
        {
            return;
        }

        IServiceProvider? services = ResolveServiceProvider();
        if (services == null)
        {
            return;
        }

        var dashboardPage = services.GetService<DashboardPage>();
        var tasksPage = services.GetService<TasksPage>();
        var peersPage = services.GetService<PeersPage>();
        var settingsPage = services.GetService<SettingsPage>();
        var onboardingPage = services.GetService<OnboardingPage>();
        var dashboardViewModel = services.GetService<DashboardViewModel>();
        var tasksViewModel = services.GetService<TasksViewModel>();
        var onboardingViewModel = services.GetService<OnboardingViewModel>();

        if (dashboardPage == null
            || tasksPage == null
            || peersPage == null
            || settingsPage == null
            || onboardingPage == null
            || dashboardViewModel == null
            || tasksViewModel == null
            || onboardingViewModel == null)
        {
            return;
        }

        ConfigureTabs(
            dashboardPage,
            tasksPage,
            peersPage,
            settingsPage,
            onboardingPage,
            dashboardViewModel,
            tasksViewModel,
            onboardingViewModel);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        TryInitializeFromServices();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        TryInitializeFromServices();
    }

    private static IServiceProvider? ResolveServiceProvider()
    {
        if (MauiApplication.Current?.Handler?.MauiContext?.Services is IServiceProvider contextServices)
        {
            return contextServices;
        }

        return MauiProgram.TryGetServiceProvider();
    }

    private void ConfigureTabs(
        DashboardPage dashboardPage,
        TasksPage tasksPage,
        PeersPage peersPage,
        SettingsPage settingsPage,
        OnboardingPage onboardingPage,
        DashboardViewModel dashboardViewModel,
        TasksViewModel tasksViewModel,
        OnboardingViewModel onboardingViewModel)
    {
        if (_tabsInitialized)
        {
            return;
        }

#if ANDROID
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.TabbedPage.SetIsSwipePagingEnabled(this, false);
#endif

        Children.Clear();

        _dashboardTab = CreateTab(dashboardPage);
        _tasksTab = CreateTab(tasksPage);
        var peersTab = CreateTab(peersPage);
        var settingsTab = CreateTab(settingsPage);

        Children.Add(_dashboardTab);
        Children.Add(_tasksTab);
        Children.Add(peersTab);
        Children.Add(settingsTab);

        _dashboardViewModel = dashboardViewModel;
        _tasksViewModel = tasksViewModel;
        _tasksPage = tasksPage;
        _onboardingPage = onboardingPage;
        _onboardingViewModel = onboardingViewModel;
        _onboardingModal = new NavigationPage(onboardingPage);
        _onboardingViewModel.CreateTaskRequested += OnCreateTaskRequested;
        _onboardingViewModel.Completed += OnOnboardingCompleted;
        _tasksPage.OnboardingTaskEditorClosed += OnOnboardingTaskEditorClosed;

        CurrentPage = _dashboardTab;
        Title = "ShuffleTask";
        _tabsInitialized = true;
    }

    public async Task ShowOnboardingLoadingAsync()
    {
        TryInitializeFromServices();
        if (_onboardingViewModel == null)
        {
            return;
        }

        _onboardingViewModel.SetStartupLoading();
        await ShowOnboardingModalAsync();
    }

    public async Task<bool> ResolveOnboardingAsync(CancellationToken cancellationToken = default)
    {
        if (_onboardingViewModel == null)
        {
            return false;
        }

        bool shouldShow = await _onboardingViewModel.LoadAsync(cancellationToken);
        if (shouldShow)
        {
            CurrentPage = _dashboardTab;
            await ShowOnboardingModalAsync();
        }
        else
        {
            await HideOnboardingModalAsync();
        }

        return shouldShow;
    }

    public async Task ShowOnboardingFailureAsync(Func<CancellationToken, Task> retry)
    {
        if (_onboardingViewModel == null)
        {
            return;
        }

        _onboardingViewModel.SetStartupFailure(retry);
        await ShowOnboardingModalAsync();
    }

    private async Task ShowOnboardingModalAsync()
    {
        if (_onboardingModal == null || Navigation.ModalStack.Contains(_onboardingModal))
        {
            return;
        }

        await Navigation.PushModalAsync(_onboardingModal, animated: false);
    }

    private async Task HideOnboardingModalAsync()
    {
        if (_onboardingModal != null && Navigation.ModalStack.LastOrDefault() == _onboardingModal)
        {
            await Navigation.PopModalAsync(animated: false);
        }
    }

    private async void OnCreateTaskRequested(object? sender, EventArgs e)
    {
        if (_tasksPage == null || _tasksTab == null)
        {
            return;
        }

        await HideOnboardingModalAsync();
        CurrentPage = _tasksTab;
        await _tasksPage.OpenNewTaskForOnboardingAsync();
    }

    private async void OnOnboardingTaskEditorClosed(object? sender, OnboardingTaskEditorClosedEventArgs e)
    {
        if (_onboardingViewModel == null)
        {
            return;
        }

        if (e.Saved)
        {
            await _onboardingViewModel.CompleteCreatedTaskAsync();
            if (_onboardingViewModel.IsVisible)
            {
                await ShowOnboardingModalAsync();
            }
            return;
        }

        _onboardingViewModel.ReturnToChoices();
        CurrentPage = _dashboardTab;
        await ShowOnboardingModalAsync();
    }

    private async void OnOnboardingCompleted(object? sender, OnboardingCompletedEventArgs e)
    {
        await HideOnboardingModalAsync();

        if (e.Outcome == OnboardingOutcome.CreatedTask)
        {
            CurrentPage = _tasksTab;
        }
        else
        {
            CurrentPage = _dashboardTab;
        }

        if (_tasksViewModel != null)
        {
            await _tasksViewModel.LoadAsync();
        }

        _dashboardViewModel?.OperationState.SetSuccess(
            e.Outcome == OnboardingOutcome.AddedSamples
                ? "Sample tasks added. Dashboard ready."
                : e.Outcome == OnboardingOutcome.ContinuedEmpty
                    ? "Dashboard ready with an empty task list."
                    : "Your first task is ready.",
            localDataSaved: true);
    }

    private static NavigationPage CreateTab(ContentPage page)
    {
        string? title = page.Title;
        var navigationPage = new NavigationPage(page)
        {
            Title = string.IsNullOrWhiteSpace(title) ? page.GetType().Name : title,
            IconImageSource = page.IconImageSource
        };

        return navigationPage;
    }

    private static async void OnExitAndStopBackgroundClicked(object sender, EventArgs e)
    {
        IServiceProvider? services = ResolveServiceProvider();
        if (services == null)
        {
            MauiApplication.Current?.Quit();
            return;
        }

        var logger = services.GetService<ILogger<MainPage>>();
        var storage = services.GetService<IStorageService>();
        var settings = services.GetService<AppSettings>();
        var coordinator = services.GetService<ShuffleCoordinatorService>();
        var clock = services.GetService<TimeProvider>();

        if (storage == null || settings == null || coordinator == null || clock == null)
        {
            logger?.LogWarning("Exit and stop background activity requested, but required services were unavailable.");
            MauiApplication.Current?.Quit();
            return;
        }

        try
        {
            logger?.LogInformation("Exit and stop background activity requested from menu.");
            settings.BackgroundActivityEnabled = false;
            settings.Touch(clock);
            await storage.SetSettingsAsync(settings);
            await coordinator.ApplyBackgroundActivityChangeAsync(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to stop background activity before exiting.");
        }
        finally
        {
            MauiApplication.Current?.Quit();
        }
    }
}
