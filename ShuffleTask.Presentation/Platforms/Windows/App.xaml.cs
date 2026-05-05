using ShuffleTask.Presentation;
using ShuffleTask.Presentation.Services;
using System.Diagnostics;
using System.Linq;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ShuffleTask.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
    private const string MainInstanceKey = "main";

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        var currentInstance = AppInstance.GetCurrent();
        var mainInstance = AppInstance.FindOrRegisterForKey(MainInstanceKey);

        if (!mainInstance.IsCurrent)
        {
            Debug.WriteLine("App(Windows): redirecting activation to existing instance.");
            _ = mainInstance.RedirectActivationToAsync(currentInstance.GetActivatedEventArgs());
            Environment.Exit(0);
            return;
        }

        mainInstance.Activated += OnAppInstanceActivated;

        InitializeComponent();
        CoreApplication.Suspending += OnSuspendingAsync;
        CoreApplication.Resuming += OnResumingAsync;
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    private static async void OnResumingAsync(object? sender, object _)
    {
        ShuffleCoordinatorService? coordinator = MauiProgram.TryGetServiceProvider()?.GetService<ShuffleCoordinatorService>();
        if (coordinator != null)
        {
            await coordinator.ResumeAsync();
        }
    }

    private static void OnSuspendingAsync(object? sender, SuspendingEventArgs e)
    {
        e.SuspendingOperation.GetDeferral().Complete();
    }

    private static void OnAppInstanceActivated(object? sender, AppActivationArguments args)
    {
        Debug.WriteLine($"App(Windows): activated via {args.Kind}.");
        (Microsoft.UI.Xaml.Application.Current as Application)?.DispatcherQueue.TryEnqueue(() =>
        {
            if (Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is Window window)
            {
                if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    presenter.Restore();
                }

                window.Activate();
                Debug.WriteLine("App(Windows): existing window activated.");
            }
        });
    }
}
