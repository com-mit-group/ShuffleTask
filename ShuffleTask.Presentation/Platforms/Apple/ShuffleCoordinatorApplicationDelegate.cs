using Microsoft.Extensions.DependencyInjection;
using ShuffleTask.Presentation.Services;
using UIKit;

namespace ShuffleTask;

internal abstract class ShuffleCoordinatorApplicationDelegate : MauiUIApplicationDelegate
{
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
}
