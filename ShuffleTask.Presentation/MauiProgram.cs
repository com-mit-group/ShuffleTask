using System;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Services;
using ShuffleTask.Application.Sync;
using ShuffleTask.Persistence;
using ShuffleTask.Presentation.Services;
using ShuffleTask.ViewModels;
using ShuffleTask.Views;
using Yaref92.Events.Abstractions;

namespace ShuffleTask;

public static partial class MauiProgram
{
    private static IServiceProvider? _services;

    public static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("Maui services have not been initialized yet.");

    public static IServiceProvider? TryGetServiceProvider()
    {
        if (_services != null)
        {
            return _services;
        }

        IServiceProvider? services = null;
        ResolvePlatformServiceProvider(ref services);
        if (services != null)
        {
            _services = services;
        }

        return _services;
    }

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

        ConfigurePlatform(builder);

        builder.ConfigureFonts(fonts =>
        {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
        });

        // DI registrations
        builder.Services.AddSingleton<TimeProvider>(_ => TimeProvider.System);
        builder.Services.AddSingleton<IShuffleLogger>(provider => new DefaultShuffleLogger(provider.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton(provider =>
        {
            var clock = provider.GetRequiredService<TimeProvider>();
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "shuffletask.db3");
            var logger = provider.GetService<IShuffleLogger>();
            return new StorageService(clock, dbPath, logger);
        });
        builder.Services.AddSingleton<IStorageService>(sp => sp.GetRequiredService<StorageService>());
        builder.Services.AddSingleton<INotificationService, NotificationService>();
        builder.Services.AddSingleton(sp => SyncOptions.LoadFromEnvironment());
        builder.Services.AddSingleton<IRealtimeSyncService>(sp =>
        {
            var clock = sp.GetRequiredService<TimeProvider>();
            var notifications = sp.GetRequiredService<INotificationService>();
            var options = sp.GetRequiredService<SyncOptions>();
            var logger = sp.GetService<IShuffleLogger>();
            var storageLazy = new Lazy<StorageService>(() => sp.GetRequiredService<StorageService>());
            return new RealtimeSyncService(clock, () => storageLazy.Value, notifications, options, logger);
        });
        builder.Services.AddSingleton<IEventAggregator>(sp => sp.GetRequiredService<IRealtimeSyncService>().Aggregator);
        builder.Services.AddSingleton<IPersistentBackgroundService, PersistentBackgroundService>();
        builder.Services.AddSingleton<ISchedulerService>(_ => new SchedulerService(deterministic: false));
        builder.Services.AddSingleton<ShuffleCoordinatorService>();

        // ViewModels
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<TasksViewModel>();
        builder.Services.AddSingleton<EditTaskViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();

        // Views
        builder.Services.AddSingleton<DashboardPage>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<TasksPage>();
        builder.Services.AddSingleton<EditTaskPage>();

#if DEBUG
        var loggingBuilder = builder.Logging;
        loggingBuilder.Services.AddLogging();
#endif

        var app = builder.Build();
        _services = app.Services;

        var syncService = _services.GetService<IRealtimeSyncService>();
        _ = syncService?.InitializeAsync();

        return app;
    }
    static partial void ConfigurePlatform(MauiAppBuilder builder);
    static partial void ResolvePlatformServiceProvider(ref IServiceProvider? services);
}
