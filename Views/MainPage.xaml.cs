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
        var settingsPage = services.GetService<SettingsPage>();

        if (dashboardPage == null || tasksPage == null || settingsPage == null)
        {
            return;
        }

        ConfigureTabs(dashboardPage, tasksPage, settingsPage);
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

        var dashboardTab = CreateTab(dashboardPage);
        var tasksTab = CreateTab(tasksPage);
        var settingsTab = CreateTab(settingsPage);

        Children.Add(dashboardTab);
        Children.Add(tasksTab);
        Children.Add(settingsTab);

        CurrentPage = dashboardTab;
        Title = "ShuffleTask";
        _tabsInitialized = true;
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
}
