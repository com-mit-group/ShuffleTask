using Foundation;
using Microsoft.Extensions.DependencyInjection;
using ShuffleTask.Services;
using UIKit;

namespace ShuffleTask;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override void OnActivated(UIApplication uiApplication)
    {
        base.OnActivated(uiApplication);

        var coordinator = MauiProgram.TryGetServiceProvider()?.GetService<ShuffleCoordinatorService>();
        if (coordinator != null)
        {
            _ = coordinator.ResumeAsync();
        }
    }

    public override void DidEnterBackground(UIApplication uiApplication)
    {
        var coordinator = MauiProgram.TryGetServiceProvider()?.GetService<ShuffleCoordinatorService>();
        if (coordinator != null)
        {
            _ = coordinator.PauseAsync();
        }

        base.DidEnterBackground(uiApplication);
    }
}
