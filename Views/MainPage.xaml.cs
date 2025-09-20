using Microsoft.Extensions.DependencyInjection;

namespace ShuffleTask.Views;

public partial class MainPage : TabbedPage
{
    public MainPage()
        : this(MauiProgram.Services.GetRequiredService<DashboardPage>(),
               MauiProgram.Services.GetRequiredService<TasksPage>(),
               MauiProgram.Services.GetRequiredService<SettingsPage>())
    {
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
