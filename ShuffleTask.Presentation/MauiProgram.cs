using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Maui.Devices;
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
using Yaref92.Events.Transport.Grpc;
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
        builder.Services.AddSingleton<PeersPage>();
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
        InitNetworkSync();

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
        builder.Services.AddSingleton<ISessionManager, SessionManager>(sp =>
        {
            var appSettings = sp.GetRequiredService<AppSettings>();
            var options = appSettings.Network ?? NetworkOptions.CreateDefault();
            return new SessionManager(
                options.ListeningPort,
                new Yaref92.Events.Sessions.ResilientSessionOptions());
        });

        // TCPEventTransport gets NetworkOptions from AppSettings
        builder.Services.AddSingleton<IEventTransport, GrpcEventTransport>(sp =>
        {
            var appSettings = sp.GetRequiredService<AppSettings>();
            var options = appSettings.Network ?? NetworkOptions.CreateDefault();
            string authSecret = options.ResolveAuthenticationSecret();
            var transport = new GrpcEventTransport(
                options.ListeningPort,
                sp.GetRequiredService<ISessionManager>(),
                new JsonEventSerializer(),
                TimeSpan.FromSeconds(20),
                authSecret);
            ConfigureLocalPlatformMetadata(transport);
            return transport;
        });
        builder.Services.AddSingleton<NetworkedEventAggregator>();
        builder.Services.AddSingleton<INetworkSyncService, NetworkSyncService>();

        builder.Services.AddSingleton<TaskStartedAsyncHandler>();
        builder.Services.AddSingleton<TimeUpNotificationAsyncHandler>();
        builder.Services.AddSingleton<TaskManifestAnnouncedAsyncHandler>();
        builder.Services.AddSingleton<TaskManifestRequestAsyncHandler>();
        builder.Services.AddSingleton<TaskBatchResponseAsyncHandler>();
    }

    private static void InitNetworkSync()
    {
        INetworkSyncService networkSyncService = _services!.GetRequiredService<INetworkSyncService>(); // force eager initialization
        var aggregator = _services!.GetRequiredService<NetworkedEventAggregator>();
        Task initTask = Task.Run(() => networkSyncService.InitAsync());

        initTask.ContinueWith((t, o) =>
        {
            aggregator.SubscribeToEventType(_services!.GetRequiredService<TaskStartedAsyncHandler>());
            aggregator.SubscribeToEventType(_services!.GetRequiredService<TimeUpNotificationAsyncHandler>());
            aggregator.SubscribeToEventType(_services!.GetRequiredService<TaskManifestAnnouncedAsyncHandler>());
            aggregator.SubscribeToEventType(_services!.GetRequiredService<TaskManifestRequestAsyncHandler>());
            aggregator.SubscribeToEventType(_services!.GetRequiredService<TaskBatchResponseAsyncHandler>());
        }, TaskScheduler.Default);
    }

    private static void ConfigureLocalPlatformMetadata(GrpcEventTransport transport)
    {
        var localPlatformProperty = typeof(GrpcEventTransport).GetProperty("LocalPlatform");
        if (localPlatformProperty?.CanWrite != true)
        {
            return;
        }

        var platform = DeviceInfo.Platform.ToString();
        var idiom = DeviceInfo.Idiom.ToString();
        var version = DeviceInfo.VersionString;
        var localPlatformValue = CreateLocalPlatformValue(localPlatformProperty.PropertyType, platform, idiom, version);
        if (localPlatformValue != null)
        {
            localPlatformProperty.SetValue(transport, localPlatformValue);
        }
    }

    private static object? CreateLocalPlatformValue(Type propertyType, string platform, string idiom, string version)
    {
        if (propertyType == typeof(string))
        {
            return $"{platform};{idiom};{version}";
        }

        if (typeof(IDictionary<string, string>).IsAssignableFrom(propertyType))
        {
            return new Dictionary<string, string>
            {
                ["Platform"] = platform,
                ["Idiom"] = idiom,
                ["Version"] = version,
            };
        }

        object? value = Activator.CreateInstance(propertyType);
        if (value is null)
        {
            return null;
        }

        SetStringPropertyIfExists(propertyType, value, "Platform", platform);
        SetStringPropertyIfExists(propertyType, value, "DevicePlatform", platform);
        SetStringPropertyIfExists(propertyType, value, "Idiom", idiom);
        SetStringPropertyIfExists(propertyType, value, "Version", version);
        SetStringPropertyIfExists(propertyType, value, "VersionString", version);
        SetStringPropertyIfExists(propertyType, value, "PlatformVersion", version);

        return value;
    }

    private static void SetStringPropertyIfExists(Type propertyType, object instance, string name, string value)
    {
        var property = propertyType.GetProperty(name);
        if (property?.CanWrite == true && property.PropertyType == typeof(string))
        {
            property.SetValue(instance, value);
        }
    }

    static partial void ConfigurePlatform(MauiAppBuilder builder);
    static partial void ResolvePlatformServiceProvider(ref IServiceProvider? services);
}
