using Microsoft.Extensions.Logging;
using ShuffleTask.Services;
using ShuffleTask.ViewModels;
using ShuffleTask.Views;

namespace ShuffleTask;

public static class MauiProgram
{
    private static IServiceProvider? _services;

    public static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("Maui services have not been initialized yet.");

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

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
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<EditTaskViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();

        // Views
        builder.Services.AddSingleton<DashboardPage>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<TasksPage>();
        builder.Services.AddSingleton<EditTaskPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        _services = app.Services;

        return app;
    }
}
