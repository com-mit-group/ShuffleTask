using ShuffleTask.ViewModels;

namespace ShuffleTask.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        Loaded += async (s, e) => await vm.LoadAsync();
    }
}
