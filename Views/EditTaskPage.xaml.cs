using ShuffleTask.ViewModels;

namespace ShuffleTask.Views;

public partial class EditTaskPage : ContentPage
{
    public EditTaskPage(EditTaskViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        vm.Saved += async (s, e) => await Navigation.PopAsync();
    }

    private async void CancelButton_Clicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private void OnClearDeadline(object sender, EventArgs e)
    {
        if (BindingContext is EditTaskViewModel vm)
        {
            vm.HasDeadline = false;
        }
    }
}
