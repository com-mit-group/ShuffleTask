namespace ShuffleTask.ViewModels;

public partial class TasksViewModel
{
    public bool IsBusy
    {
        get => isBusy;
        set => SetProperty(ref isBusy, value);
    }
}
