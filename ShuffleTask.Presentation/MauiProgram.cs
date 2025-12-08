using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Services;
using ShuffleTask.Persistence;
using ShuffleTask.Presentation.EventsHandlers;
using ShuffleTask.Presentation.Services;
using ShuffleTask.ViewModels;
using ShuffleTask.Views;
using Yaref92.Events;
using Yaref92.Events.Abstractions;
using Yaref92.Events.Serialization;
using Yaref92.Events.Transports;

namespace ShuffleTask.Presentation;

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
        builder.Services.AddSingleton(provider =>
        {
            var clock = provider.GetRequiredService<TimeProvider>();
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "shuffletask.db3");
            return new StorageService(clock, dbPath);
        });
        builder.Services.AddSingleton<IStorageService>(sp => sp.GetRequiredService<StorageService>());
        builder.Services.AddSingleton<INotificationService, NotificationService>();
        builder.Services.AddSingleton<IPersistentBackgroundService, PersistentBackgroundService>();
        SetupEventAggregation(builder);
        builder.Services.AddSingleton<ISchedulerService>(sp => new SchedulerService(deterministic: false));
        builder.Services.AddSingleton<ShuffleCoordinatorService>();

        // ViewModels
        builder.Services.AddSingleton<DashboardViewModel>(sp =>
        {
            var storage = sp.GetRequiredService<IStorageService>();
            var scheduler = sp.GetRequiredService<ISchedulerService>();
            var notifications = sp.GetRequiredService<INotificationService>();
            var shuffleCoordinator = sp.GetRequiredService<ShuffleCoordinatorService>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();
            var networkSync = sp.GetRequiredService<INetworkSyncService>();
            var appSettings = sp.GetRequiredService<AppSettings>();
            var dashboardViewModel = new DashboardViewModel(storage, scheduler, notifications, shuffleCoordinator, timeProvider, networkSync, appSettings);
            var taskStartedHandler = sp.GetRequiredService<TaskStartedAsyncHandler>();
            taskStartedHandler.RegisterDashboard(dashboardViewModel);
            return dashboardViewModel;
        });
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

        return app;
    }

    private static void SetupEventAggregation(MauiAppBuilder builder)
    {
        // Register AppSettings as singleton, loaded from storage
        builder.Services.AddSingleton<AppSettings>(sp =>
        {
            var storage = sp.GetRequiredService<IStorageService>();
            // Load synchronously during DI setup
            AppSettings settings = null;
            Task.Run(async () =>
            {
                await storage.InitializeAsync().ConfigureAwait(false);
                settings = await storage.GetSettingsAsync().ConfigureAwait(false);
                settings.Network ??= NetworkOptions.CreateDefault();
                settings.Network.Normalize();
            }).Wait();
            return settings;
        });
        
        builder.Services.AddSingleton<IEventAggregator, EventAggregator>();
        
        // TCPEventTransport gets NetworkOptions from AppSettings
        builder.Services.AddSingleton<IEventTransport, TCPEventTransport>(sp =>
        {
            var appSettings = sp.GetRequiredService<AppSettings>();
            var options = appSettings.Network ?? NetworkOptions.CreateDefault();
            string authSecret = options.ResolveAuthenticationSecret();
            return new TCPEventTransport(
                options.ListeningPort,
                new JsonEventSerializer(),
                TimeSpan.FromSeconds(20),
                authSecret);
        });
        builder.Services.AddSingleton<NetworkedEventAggregator>();
        builder.Services.AddSingleton<INetworkSyncService, NetworkSyncService>();
        builder.Services.AddSingleton<TaskStartedAsyncHandler>(sp =>
        {
            var logger = sp.GetService<ILogger<NetworkSyncService>>();
            var storage = sp.GetRequiredService<StorageService>();
            var notifications = sp.GetRequiredService<INotificationService>();
            var appSettings = sp.GetRequiredService<AppSettings>();
            var taskStartedAsyncHandler = new TaskStartedAsyncHandler(logger, storage, notifications, appSettings);
            return taskStartedAsyncHandler;
        });
        builder.Services.AddSingleton<TimeUpNotificationAsyncHandler>(sp =>
        {
            var logger = sp.GetService<ILogger<NetworkSyncService>>();
            var storage = sp.GetRequiredService<StorageService>();
            var notifications = sp.GetRequiredService<INotificationService>();
            var appSettings = sp.GetRequiredService<AppSettings>();
            var timeUpNotificationAsyncHandler = new TimeUpNotificationAsyncHandler(logger, storage, notifications, appSettings);

            var ns = sp.GetRequiredService<INetworkSyncService>();
            Task initTask = Task.Run(() => ns.InitAsync());
            initTask.ContinueWith((t, o) =>
            {
                var aggregator = sp.GetRequiredService<NetworkedEventAggregator>();
                var taskStartedAsyncHandler = sp.GetRequiredService<TaskStartedAsyncHandler>();
                aggregator.SubscribeToEventType(taskStartedAsyncHandler);
                aggregator.SubscribeToEventType(timeUpNotificationAsyncHandler);
            }, TaskScheduler.Default);
            return timeUpNotificationAsyncHandler;
        });
    }

    static partial void ConfigurePlatform(MauiAppBuilder builder);
    static partial void ResolvePlatformServiceProvider(ref IServiceProvider? services);
}
