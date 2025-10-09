#if WINDOWS
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Platform;
using ShuffleTask.Presentation.Services;

namespace ShuffleTask;

public static partial class MauiProgram
{
    static partial void ConfigurePlatform(MauiAppBuilder builder)
    {
        builder.Services.AddSingleton<WindowsTrayIconManager>();

        builder.ConfigureLifecycleEvents(events =>
        {
            events.AddWindows(windows =>
            {
                windows.OnWindowCreated(window =>
                {
                    if (window is not MauiWinUIWindow mauiWindow)
                    {
                        return;
                    }

                    var services = TryGetServiceProvider()
                                   ?? (Microsoft.UI.Xaml.Application.Current as MauiWinUIApplication)?.Services;
                    var trayManager = services?.GetService<WindowsTrayIconManager>();
                    trayManager?.Initialize(mauiWindow);
                });
            });
        });
    }

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
