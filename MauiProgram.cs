using Microsoft.Extensions.Logging;
using Plugin.LocalNotification;
using ShuffleTask.Services;
using ShuffleTask.ViewModels;
using ShuffleTask.Views;

namespace ShuffleTask;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

#if !WINDOWS
        // Only initialize Plugin.LocalNotification on non-Windows platforms
        builder.UseLocalNotification();
#endif

        builder.ConfigureFonts(fonts =>
        {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
        });

        // DI registrations
        builder.Services.AddSingleton<StorageService>();
        builder.Services.AddSingleton<NotificationService>();
        builder.Services.AddSingleton(sp => new SchedulerService(deterministic: false));

        // ViewModels
        builder.Services.AddSingleton<NowViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<EditTaskViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();

        // Views
        builder.Services.AddSingleton<NowPage>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<TasksPage>();
        builder.Services.AddSingleton<EditTaskPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
