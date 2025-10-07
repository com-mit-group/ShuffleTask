#if IOS || MACCATALYST
using System;
using Microsoft.Maui;
using UIKit;

namespace ShuffleTask;

public static partial class MauiProgram
{
    static partial void ResolvePlatformServiceProvider(ref IServiceProvider? services)
    {
        if (services != null)
        {
            return;
        }

        if (MauiUIApplicationDelegate.Current is MauiUIApplicationDelegate appDelegate)
        {
            services = appDelegate.Services;
            return;
        }

        if (UIApplication.SharedApplication?.Delegate is MauiUIApplicationDelegate delegateInstance)
        {
            services = delegateInstance.Services;
        }
    }
}
#endif
