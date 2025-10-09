using ShuffleTask.Domain.Entities;
using ShuffleTask.ViewModels;

namespace ShuffleTask.Views;

public partial class TasksPage : ContentPage
{
    private readonly TasksViewModel _vm;
    private readonly IServiceProvider _services;

    public TasksPage(TasksViewModel vm, IServiceProvider services)
    {
        InitializeComponent();
        _vm = vm;
        _services = services;
        BindingContext = _vm;

        Appearing += OnAppearing;
    }

    private async void OnAppearing(object? sender, EventArgs e)
    {
        await _vm.LoadAsync();
    }

    private async void OnAddClicked(object? sender, EventArgs e)
    {
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

    private async Task OpenEditorAsync(TaskItem? task)
    {
        var page = _services.GetRequiredService<EditTaskPage>();
        var editorVm = _services.GetRequiredService<EditTaskViewModel>();
        editorVm.Load(task);
        editorVm.Saved -= OnEditorSaved;
        editorVm.Saved += OnEditorSaved;

        void OnEditorPageDisappearing(object? s, EventArgs e)
        {
            editorVm.Saved -= OnEditorSaved;
            page.Disappearing -= OnEditorPageDisappearing;
        }

        page.Disappearing -= OnEditorPageDisappearing;
        page.Disappearing += OnEditorPageDisappearing;

        page.BindingContext = editorVm;
        page.Title = editorVm.IsNew ? "New Task" : "Edit Task";
        await Navigation.PushAsync(page);
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
