using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Services;
using ShuffleTask.Presentation.Services;
using ShuffleTask.ViewModels;

namespace ShuffleTask.Presentation;

public static class SharedPresentationServiceCollectionExtensions
{
    public static IServiceCollection AddShuffleTaskSharedPresentation(
        this IServiceCollection services,
        Action<IServiceProvider, DashboardViewModel>? configureDashboard = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<TimeProvider>(_ => TimeProvider.System);
        services.TryAddSingleton<ISchedulerService>(_ => new SchedulerService(deterministic: false));
        services.TryAddSingleton<ShuffleCoordinatorService>();
        services.TryAddSingleton<TasksViewModel>();
        services.TryAddSingleton<EditTaskViewModel>();
        services.TryAddSingleton<PeriodDefinitionEditorViewModel>();
        services.TryAddSingleton(sp =>
        {
            var dashboardViewModel = new DashboardViewModel(
                sp.GetRequiredService<IStorageService>(),
                sp.GetRequiredService<ISchedulerService>(),
                sp.GetRequiredService<INotificationService>(),
                sp.GetRequiredService<ShuffleCoordinatorService>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<INetworkSyncService>(),
                sp.GetRequiredService<AppSettings>());

            configureDashboard?.Invoke(sp, dashboardViewModel);
            return dashboardViewModel;
        });

        return services;
    }
}
