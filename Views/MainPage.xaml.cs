using Microsoft.Extensions.DependencyInjection;

namespace ShuffleTask.Views;

public partial class MainPage : TabbedPage
{
    public MainPage()
        : base()
    {
        if (MauiProgram.Services == null)
        {
            throw new InvalidOperationException("Service provider is not initialized. Cannot create MainPage.");
        }
        try
        {
            var dashboardPage = MauiProgram.Services.GetRequiredService<DashboardPage>();
            var tasksPage = MauiProgram.Services.GetRequiredService<TasksPage>();
            var settingsPage = MauiProgram.Services.GetRequiredService<SettingsPage>();
            InitializeComponent();
#if ANDROID
            Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.TabbedPage.SetIsSwipePagingEnabled(this, false);
#endif
            Children.Add(new NavigationPage(dashboardPage));
            Children.Add(new NavigationPage(tasksPage));
            Children.Add(new NavigationPage(settingsPage));
            Title = "ShuffleTask";
        }
        catch (InvalidOperationException ex)
        {
            // Optionally log the exception or handle it as needed
            throw new InvalidOperationException("Failed to resolve required services for MainPage.", ex);
        }
    }

    public MainPage(DashboardPage dashboardPage, TasksPage tasksPage, SettingsPage settingsPage)
    {
        InitializeComponent();

#if ANDROID
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.TabbedPage.SetIsSwipePagingEnabled(this, false);
#endif

        Children.Add(new NavigationPage(dashboardPage));
        Children.Add(new NavigationPage(tasksPage));
        Children.Add(new NavigationPage(settingsPage));
        Title = "ShuffleTask";
    }
}
