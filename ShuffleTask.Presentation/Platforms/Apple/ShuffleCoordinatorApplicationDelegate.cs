using System;
using BackgroundTasks;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using ShuffleTask.Presentation.Services;
using UIKit;

namespace ShuffleTask;

internal abstract class ShuffleCoordinatorApplicationDelegate : MauiUIApplicationDelegate
{
    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        var result = base.FinishedLaunching(application, launchOptions);

        EnsurePersistentBackgroundInitialized();

        return result;
    }

    public override void HandleEventsForBackgroundTasks(UIApplication application, NSSet<BGTask> backgroundTasks)
    {
        EnsurePersistentBackgroundInitialized();

        foreach (var task in backgroundTasks)
        {
            if (!string.Equals(task.Identifier, PersistentBackgroundService.AppleTaskIdentifier, StringComparison.Ordinal))
            {
                task.SetTaskCompleted(false);
            }
        }
    }

    public override void OnActivated(UIApplication uiApplication)
    {
        base.OnActivated(uiApplication);

        ResumeCoordinator();
    }

    // Note: We no longer pause the coordinator when the app enters background.
    // The ShuffleCoordinatorService schedules notifications via UNUserNotificationCenter,
    // which delivers notifications even when the app is backgrounded.
    // This ensures auto-shuffle notifications fire reliably in the background.

    private static void ResumeCoordinator()
    {
        var coordinator = MauiProgram.TryGetServiceProvider()?.GetService<ShuffleCoordinatorService>();
        if (coordinator != null)
        {
            _ = coordinator.ResumeAsync();
        }
    }

    private static void EnsurePersistentBackgroundInitialized()
    {
        var provider = MauiProgram.TryGetServiceProvider();
        if (provider == null)
        {
            return;
        }

        var backgroundService = provider.GetService<IPersistentBackgroundService>();
        if (backgroundService == null)
        {
            return;
        }

        try
        {
            backgroundService.InitializeAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize persistent background service: {ex}");
        }
    }
}
