using Android.App;
using Android.Content.PM;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using ShuffleTask.Presentation.Services;

namespace ShuffleTask;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnResume()
    {
        base.OnResume();

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
}
