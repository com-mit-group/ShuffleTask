using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Presentation;
using ShuffleTask.Presentation.Services;
using ShuffleTask.ViewModels;

namespace ShuffleTask.Presentation.Tests;

public class SharedPresentationRegistrationTests
{
    [Test]
    public void AddShuffleTaskSharedPresentation_RegistersReusablePresentationServicesOnly()
    {
        var services = new ServiceCollection();

        services.AddShuffleTaskSharedPresentation();

        Assert.That(services.Any(ServiceType<TimeProvider>), Is.True);
        Assert.That(services.Any(ServiceType<ISchedulerService>), Is.True);
        Assert.That(services.Any(ServiceType<ShuffleCoordinatorService>), Is.True);
        Assert.That(services.Any(ServiceType<DashboardViewModel>), Is.True);
        Assert.That(services.Any(ServiceType<TasksViewModel>), Is.True);
        Assert.That(services.Any(ServiceType<EditTaskViewModel>), Is.True);
        Assert.That(services.Any(ServiceType<PeriodDefinitionEditorViewModel>), Is.True);
        Assert.That(services.Any(d => d.ServiceType.Name == "SettingsViewModel"), Is.False);
        Assert.That(services.Any(d => d.ServiceType.Namespace == "ShuffleTask.Views"), Is.False);
    }

    [Test]
    public void AddShuffleTaskSharedPresentation_InvokesDashboardHostHookWhenDashboardIsResolved()
    {
        var services = new ServiceCollection();
        var hookWasCalled = false;

        services.AddSingleton(Substitute.For<IStorageService>());
        services.AddSingleton(Substitute.For<INotificationService>());
        services.AddSingleton(Substitute.For<IPersistentBackgroundService>());
        services.AddSingleton(Substitute.For<INetworkSyncService>());
        services.AddSingleton(new AppSettings());
        services.AddShuffleTaskSharedPresentation((provider, dashboard) =>
        {
            Assert.That(provider, Is.Not.Null);
            Assert.That(dashboard, Is.Not.Null);
            hookWasCalled = true;
        });

        using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<DashboardViewModel>();

        Assert.That(hookWasCalled, Is.True);
    }

    private static Func<ServiceDescriptor, bool> ServiceType<T>() => descriptor => descriptor.ServiceType == typeof(T);
}
