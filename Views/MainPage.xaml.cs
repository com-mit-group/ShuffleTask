using Microsoft.Extensions.DependencyInjection;

namespace ShuffleTask.Views;

public partial class MainPage : TabbedPage
{
    private bool _tabsInitialized;

    public MainPage()
    {
        InitializeComponent();
        TryInitializeFromServices();
    }

    public MainPage(DashboardPage dashboardPage, TasksPage tasksPage, SettingsPage settingsPage)
    {
        InitializeComponent();
        ConfigureTabs(dashboardPage, tasksPage, settingsPage);
    }

    private bool TryInitializeFromServices()
    {
        if (_tabsInitialized)
        {
            return true;
        }

        IServiceProvider? services = ResolveServiceProvider();
        if (services == null)
        {
            return false;
        }

        var dashboardPage = services.GetService<DashboardPage>();
        var tasksPage = services.GetService<TasksPage>();
        var settingsPage = services.GetService<SettingsPage>();

        if (dashboardPage == null || tasksPage == null || settingsPage == null)
        {
            return false;
        }

        ConfigureTabs(dashboardPage, tasksPage, settingsPage);
        return true;
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
        if (Application.Current?.Handler?.MauiContext?.Services is IServiceProvider contextServices)
        {
            return contextServices;
        }

        return MauiProgram.TryGetServiceProvider();
    }

    private void ConfigureTabs(DashboardPage dashboardPage, TasksPage tasksPage, SettingsPage settingsPage)
    {
        if (_tabsInitialized)
        {
            return;
        }

#if ANDROID
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.TabbedPage.SetIsSwipePagingEnabled(this, false);
#endif

        Children.Clear();
        Children.Add(new NavigationPage(dashboardPage));
        Children.Add(new NavigationPage(tasksPage));
        Children.Add(new NavigationPage(settingsPage));
        Title = "ShuffleTask";
        _tabsInitialized = true;
    }
}
