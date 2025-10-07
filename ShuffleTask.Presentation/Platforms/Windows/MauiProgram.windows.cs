#if WINDOWS
using System;
using Microsoft.Maui;
using Microsoft.UI.Xaml;

namespace ShuffleTask;

public static partial class MauiProgram
{
    static partial void ResolvePlatformServiceProvider(ref IServiceProvider? services)
    {
        if (services != null)
        {
            return;
        }

        if (Application.Current is MauiWinUIApplication winApp)
        {
            services = winApp.Services;
        }
    }
}
#endif
