using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using ShuffleTask.Presentation.Services;
using System.Diagnostics;

namespace ShuffleTask.Presentation;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTask, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        LogActivationIntent("OnCreate", Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        LogActivationIntent("OnNewIntent", intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        LogActivationIntent("OnResume", Intent);

        var coordinator = MauiProgram.TryGetServiceProvider()?.GetService<ShuffleCoordinatorService>();
        if (coordinator != null)
        {
            _ = coordinator.ResumeAsync();
        }
    }

    // Note: We no longer pause the coordinator when the activity pauses.
    // The ShuffleCoordinatorService schedules notifications via AlarmManager,
    // which delivers notifications even when the app is backgrounded.
    // This ensures auto-shuffle notifications fire reliably in the background.

    private static void LogActivationIntent(string source, Intent? intent)
    {
        Debug.WriteLine($"MainActivity(Android): {source}, action={intent?.Action}, flags={intent?.Flags}");
    }
}
