namespace ShuffleTask.Views;

public partial class MainPage : Microsoft.Maui.Controls.TabbedPage
{
    public MainPage(NowPage nowPage, TasksPage tasksPage, SettingsPage settingsPage)
    {
        InitializeComponent();

#if ANDROID
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.TabbedPage.SetIsSwipePagingEnabled(this, false);
#endif

        Children.Add(nowPage);
        Children.Add(new Microsoft.Maui.Controls.NavigationPage(tasksPage) { Title = "Tasks" });
        Children.Add(new Microsoft.Maui.Controls.NavigationPage(settingsPage) { Title = "Settings" });
        Title = "ShuffleTask";
    }
}
