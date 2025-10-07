#if IOS || MACCATALYST
using System;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
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

        static IServiceProvider? ResolveOnMainThread()
        {
            if (MauiUIApplicationDelegate.Current is MauiUIApplicationDelegate appDelegate)
            {
                return appDelegate.Services;
            }

            if (UIApplication.SharedApplication?.Delegate is MauiUIApplicationDelegate delegateInstance)
            {
                return delegateInstance.Services;
            }

            return null;
        }

        if (MainThread.IsMainThread)
        {
            services = ResolveOnMainThread();
        }
        else
        {
            services = MainThread.InvokeOnMainThreadAsync(ResolveOnMainThread).GetAwaiter().GetResult();
        }
    }
}
#endif
