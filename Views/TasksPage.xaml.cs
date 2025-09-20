using ShuffleTask.Models;
using ShuffleTask.ViewModels;

namespace ShuffleTask.Views;

public partial class TasksPage : ContentPage
{
    private readonly MainViewModel _vm;
    private readonly IServiceProvider _sp;

    public TasksPage(MainViewModel vm, IServiceProvider sp)
    {
        InitializeComponent();
        _vm = vm;
        _sp = sp;
        BindingContext = _vm;

        Appearing += async (s, e) => await _vm.LoadAsync();

        if (this.FindByName("AddToolbar") is ToolbarItem add)
        {
            add.Clicked += OnAddClicked;
        }
    }

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        var (page, editVm) = ResolveEditTaskPage();
        editVm.Task = new TaskItem();
        await Navigation.PushAsync(page);
    }

    private async void OnEditSwipe(object sender, EventArgs e)
    {
        if (sender is SwipeItem si && si.CommandParameter is TaskItem t)
        {
            var (page, editVm) = ResolveEditTaskPage();
            // Clone object for editing; only persist on Save
            editVm.Task = new TaskItem
            {
                Id = t.Id,
                Title = t.Title,
                Importance = t.Importance,
                Deadline = t.Deadline,
                Repeat = t.Repeat,
                Weekdays = t.Weekdays,
                IntervalDays = t.IntervalDays,
                LastDoneAt = t.LastDoneAt,
                AllowedPeriod = t.AllowedPeriod,
                Paused = t.Paused,
                CreatedAt = t.CreatedAt
            };
            await Navigation.PushAsync(page);
        }
    }

    private async void OnPauseResumeSwipe(object sender, EventArgs e)
    {
        if (sender is SwipeItem si && si.CommandParameter is TaskItem t)
        {
            await _vm.PauseResumeAsync(t);
        }
    }

    private async void OnDeleteSwipe(object sender, EventArgs e)
    {
        if (sender is SwipeItem si && si.CommandParameter is TaskItem t)
        {
            bool confirm = await DisplayAlert("Delete Task", $"Delete '{t.Title}'?", "Delete", "Cancel");
            if (!confirm) return;
            await _vm.DeleteAsync(t);
        }
    }

    private (EditTaskPage Page, EditTaskViewModel ViewModel) ResolveEditTaskPage()
    {
        var page = _sp.GetRequiredService<EditTaskPage>();
        if (page.BindingContext is not EditTaskViewModel editVm)
        {
            throw new InvalidOperationException("EditTaskPage must have an EditTaskViewModel binding context");
        }

        editVm.Saved -= OnEditTaskSaved;
        editVm.Saved += OnEditTaskSaved;
        return (page, editVm);
    }

    private async void OnEditTaskSaved(object? sender, EventArgs e)
    {
        await _vm.LoadAsync();
    }
}
