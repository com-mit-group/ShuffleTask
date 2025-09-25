using Foundation;

namespace ShuffleTask;

[Register("AppDelegate")]
public class AppDelegate : ShuffleCoordinatorApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
