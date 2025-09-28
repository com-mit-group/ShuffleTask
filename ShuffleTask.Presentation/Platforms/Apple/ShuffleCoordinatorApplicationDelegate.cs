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

    public override void DidEnterBackground(UIApplication uiApplication)
    {
        PauseCoordinator();

        base.DidEnterBackground(uiApplication);
    }

    private static void ResumeCoordinator()
    {
        var coordinator = MauiProgram.TryGetServiceProvider()?.GetService<ShuffleCoordinatorService>();
        if (coordinator != null)
        {
            _ = coordinator.ResumeAsync();
        }
    }

    private static void PauseCoordinator()
    {
        var coordinator = MauiProgram.TryGetServiceProvider()?.GetService<ShuffleCoordinatorService>();
        if (coordinator != null)
        {
            _ = coordinator.PauseAsync();
        }
    }
}
