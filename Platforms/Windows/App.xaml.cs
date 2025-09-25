using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using ShuffleTask.Services;
using Windows.ApplicationModel;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ShuffleTask.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        this.InitializeComponent();
        this.Suspending += OnSuspending;
        this.Resuming += OnResuming;
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    private static void OnResuming(object? _, object _2)
    {
        var coordinator = MauiProgram.TryGetServiceProvider()?.GetService<ShuffleCoordinatorService>();
        if (coordinator != null)
        {
            _ = coordinator.ResumeAsync();
        }
    }

    private static void OnSuspending(object? _, SuspendingEventArgs e)
    {
        var coordinator = MauiProgram.TryGetServiceProvider()?.GetService<ShuffleCoordinatorService>();
        if (coordinator == null)
        {
            return;
        }

        var deferral = e.SuspendingOperation.GetDeferral();
        _ = coordinator.PauseAsync().ContinueWith(_ => deferral.Complete());
    }
}
