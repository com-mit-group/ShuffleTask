using System.ComponentModel;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Presentation.Models;
using ShuffleTask.Presentation.Utilities;
using ShuffleTask.ViewModels;

namespace ShuffleTask.Views;

public partial class TasksPage : ContentPage
{
    private readonly TasksViewModel _vm;
    private readonly IServiceProvider _services;
    private bool _onboardingEditorOpen;
    private bool _skipNextAppearingLoad;
#if WINDOWS
    private Microsoft.UI.Xaml.Controls.TextBox? _quickAddPlatformEntry;
#endif

    public event EventHandler<OnboardingTaskEditorClosedEventArgs>? OnboardingTaskEditorClosed;

    public TasksPage(TasksViewModel vm, IServiceProvider services)
    {
        InitializeComponent();
        _vm = vm;
        _services = services;
        BindingContext = _vm;

        Appearing += OnAppearing;
        _vm.OperationState.PropertyChanged += OnOperationStateChanged;
        _vm.QuickAddOperationState.PropertyChanged += OnQuickAddOperationStateChanged;
        QuickAddEntry.HandlerChanged += OnQuickAddEntryHandlerChanged;
    }

    private void OnQuickAddOperationStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OperationState.Announcement)
            || string.IsNullOrWhiteSpace(_vm.QuickAddOperationState.Announcement))
        {
            return;
        }

        OperationStateAccessibility.Announce(Dispatcher, QuickAddStateMessage, _vm.QuickAddOperationState);
        if (_vm.QuickAddOperationState.LocalDataSaved == true)
        {
            Dispatcher.Dispatch(() => QuickAddEntry.Focus());
        }
    }

    private void OnOperationStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OperationState.Announcement)
            || string.IsNullOrWhiteSpace(_vm.OperationState.Announcement))
        {
            return;
        }

        OperationStateAccessibility.Announce(Dispatcher, OperationStateMessage, _vm.OperationState);
    }

    private async void OnAppearing(object? sender, EventArgs e)
    {
        if (_skipNextAppearingLoad)
        {
            _skipNextAppearingLoad = false;
            return;
        }

        await _vm.LoadAsync();
    }

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        await OpenEditorAsync(null);
    }

    private async void OnQuickAddClicked(object? sender, EventArgs e)
    {
        await SubmitQuickAddAsync();
    }

    private async void OnQuickAddCompleted(object? sender, EventArgs e)
    {
        await SubmitQuickAddAsync();
    }

    private async Task SubmitQuickAddAsync()
    {
        await _vm.QuickAddAsync();
        QuickAddEntry.Focus();
    }

    private void OnQuickAddEntryHandlerChanged(object? sender, EventArgs e)
    {
#if WINDOWS
        if (_quickAddPlatformEntry is not null)
        {
            _quickAddPlatformEntry.KeyDown -= OnQuickAddPlatformKeyDown;
        }

        _quickAddPlatformEntry = QuickAddEntry.Handler?.PlatformView as Microsoft.UI.Xaml.Controls.TextBox;
        if (_quickAddPlatformEntry is not null)
        {
            _quickAddPlatformEntry.KeyDown += OnQuickAddPlatformKeyDown;
        }
#endif
    }

#if WINDOWS
    private void OnQuickAddPlatformKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape || string.IsNullOrEmpty(_vm.QuickAddTitle))
        {
            return;
        }

        e.Handled = true;
        Dispatcher.Dispatch(async () =>
        {
            bool clear = await DisplayAlert("Clear quick add?", "Discard the quick task title?", "Clear", "Keep editing");
            if (clear)
            {
                _vm.ClearQuickAdd();
            }

            QuickAddEntry.Focus();
        });
    }
#endif

    public async Task OpenNewTaskForOnboardingAsync()
    {
        if (_onboardingEditorOpen)
        {
            return;
        }

        _onboardingEditorOpen = true;
        await OpenEditorAsync(null);
    }

    private async void OnEditButtonClicked(object sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: TaskItem task })
        {
            await OpenEditorAsync(TaskItem.Clone(task));
        }
    }

    private async void OnResumeButtonClicked(object sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: TaskItem task })
        {
            await _vm.ResumeAsync(task);
        }
    }

    private async void OnMarkDoneButtonClicked(object sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: TaskItem task })
        {
            await _vm.MarkDoneAsync(task);
        }
    }

    private async void OnEditSwipe(object sender, EventArgs e)
    {
        if (sender is SwipeItem { CommandParameter: TaskItem task })
        {
            await OpenEditorAsync(TaskItem.Clone(task));
        }
    }

    private async void OnTogglePauseSwipe(object sender, EventArgs e)
    {
        if (sender is SwipeItem { CommandParameter: TaskItem task })
        {
            await _vm.TogglePauseAsync(task);
        }
    }

    private async void OnCutInOnceSwipe(object sender, EventArgs e)
    {
        if (sender is SwipeItem { CommandParameter: TaskItem task })
        {
            await _vm.SetCutInLineModeAsync(task, CutInLineMode.Once);
        }
    }

    private async void OnCutInUntilDoneSwipe(object sender, EventArgs e)
    {
        if (sender is SwipeItem { CommandParameter: TaskItem task })
        {
            await _vm.SetCutInLineModeAsync(task, CutInLineMode.UntilCompletion);
        }
    }

    private async void OnClearCutInSwipe(object sender, EventArgs e)
    {
        if (sender is SwipeItem { CommandParameter: TaskItem task })
        {
            await _vm.SetCutInLineModeAsync(task, CutInLineMode.None);
        }
    }

    private async void OnDeleteSwipe(object sender, EventArgs e)
    {
        var task = sender switch
        {
            SwipeItem { CommandParameter: TaskItem swipeTask } => swipeTask,
            Button { CommandParameter: TaskItem buttonTask } => buttonTask,
            _ => null
        };

        if (task is null)
        {
            return;
        }

        bool confirm = await DisplayAlert("Delete Task", $"Delete '{task.Title}'?", "Delete", "Cancel");
        if (!confirm)
        {
            return;
        }

        await _vm.DeleteAsync(task);
    }

    private void OnResetFiltersClicked(object sender, EventArgs e)
    {
        _vm.ResetFilters();
    }

    private async Task OpenEditorAsync(TaskItem? task)
    {
        var page = _services.GetRequiredService<EditTaskPage>();
        var editorVm = _services.GetRequiredService<EditTaskViewModel>();
        await editorVm.LoadAsync(task);
        editorVm.Saved -= OnEditorSaved;
        editorVm.Saved += OnEditorSaved;

        if (Parent is not NavigationPage navigationPage)
        {
            throw new InvalidOperationException("The Tasks page requires a navigation host to open the task editor.");
        }

        void OnEditorPopped(object? sender, NavigationEventArgs e)
        {
            if (e.Page != page)
            {
                return;
            }

            editorVm.Saved -= OnEditorSaved;
            navigationPage.Popped -= OnEditorPopped;
            _skipNextAppearingLoad = true;
            if (!_onboardingEditorOpen)
            {
                Dispatcher.Dispatch(() => QuickAddEntry.Focus());
                return;
            }

            _onboardingEditorOpen = false;
            OnboardingTaskEditorClosed?.Invoke(
                this,
                new OnboardingTaskEditorClosedEventArgs(page.WasSavedBeforeClosing));
        }

        navigationPage.Popped += OnEditorPopped;

        page.BindingContext = editorVm;
        page.Title = editorVm.IsNew ? "New Task" : "Edit Task";
        try
        {
            await navigationPage.PushAsync(page);
        }
        catch
        {
            editorVm.Saved -= OnEditorSaved;
            navigationPage.Popped -= OnEditorPopped;
            if (_onboardingEditorOpen)
            {
                _onboardingEditorOpen = false;
                OnboardingTaskEditorClosed?.Invoke(this, new OnboardingTaskEditorClosedEventArgs(false));
            }

            throw;
        }
    }

    private async void OnEditorSaved(object? sender, EventArgs e)
    {
        if (sender is EditTaskViewModel vm)
        {
            vm.Saved -= OnEditorSaved;
        }

        await _vm.LoadAsync();
    }
}

public sealed class OnboardingTaskEditorClosedEventArgs(bool saved) : EventArgs
{
    public bool Saved { get; } = saved;
}
