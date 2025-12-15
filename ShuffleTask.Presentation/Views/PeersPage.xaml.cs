using ShuffleTask.ViewModels;

namespace ShuffleTask.Views;

public partial class PeersPage : ContentPage
{
    private readonly SettingsViewModel _vm;

    public PeersPage(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        await _vm.LoadCommand.ExecuteAsync(null);
    }
}
