using Android.App;
using Android.Content.PM;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using ShuffleTask.Services;

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

    protected override void OnPause()
    {
        var coordinator = MauiProgram.TryGetServiceProvider()?.GetService<ShuffleCoordinatorService>();
        if (coordinator != null)
        {
            _ = coordinator.PauseAsync();
        }

        base.OnPause();
    }
}
