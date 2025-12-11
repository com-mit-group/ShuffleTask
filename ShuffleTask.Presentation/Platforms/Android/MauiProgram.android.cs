#if ANDROID
using System;
using Android.App;
using Microsoft.Maui;

namespace ShuffleTask.Presentation;

public static partial class MauiProgram
{
    static partial void ResolvePlatformServiceProvider(ref IServiceProvider? services)
    {
        if (services != null)
        {
            return;
        }

        if (MauiApplication.Current is MauiApplication current)
        {
            #pragma warning disable CS0618
            services = current.Services;
            #pragma warning restore CS0618
            return;
        }

        if (global::Android.App.Application.Context is MauiApplication contextApp)
        {
            #pragma warning disable CS0618
            services = contextApp.Services;
            #pragma warning restore CS0618
        }
    }
}
#endif
