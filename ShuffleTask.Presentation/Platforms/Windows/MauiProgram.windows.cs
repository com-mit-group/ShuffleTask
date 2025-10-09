#if WINDOWS
namespace ShuffleTask;

public static partial class MauiProgram
{
    static partial void ResolvePlatformServiceProvider(ref IServiceProvider? services)
    {
        if (services != null)
        {
            return;
        }

        if (Microsoft.UI.Xaml.Application.Current is MauiWinUIApplication winApp)
        {
            services = winApp.Services;
        }
    }
}
#endif
