using System.ComponentModel;
using ShuffleTask.ViewModels;

namespace ShuffleTask.Views;

public partial class EditTaskPage : ContentPage
{
    private EditTaskViewModel? _viewModel;
    private bool _eventsSubscribed;

    public EditTaskPage(EditTaskViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (BindingContext is EditTaskViewModel vm)
        {
            _viewModel = vm;
            SubscribeToViewModel();
        }

        UpdateTitle();
    }

    protected override void OnBindingContextChanged()
    {
        UnsubscribeFromViewModel();
        base.OnBindingContextChanged();

        _viewModel = BindingContext as EditTaskViewModel;
        SubscribeToViewModel();
    }

    private async void OnSaved(object? sender, EventArgs e)
    {
        UnsubscribeFromViewModel();
        await Navigation.PopAsync();
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        UnsubscribeFromViewModel();
        await Navigation.PopAsync();
    }

    private void OnClearDeadline(object sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.HasDeadline = false;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditTaskViewModel.IsNew))
        {
            UpdateTitle();
        }
    }

    private void SubscribeToViewModel()
    {
        if (_viewModel == null || _eventsSubscribed)
        {
            return;
        }

        _viewModel.Saved += OnSaved;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _eventsSubscribed = true;

        UpdateTitle();
    }

    private void UnsubscribeFromViewModel()
    {
        if (_viewModel == null || !_eventsSubscribed)
        {
            return;
        }

        _viewModel.Saved -= OnSaved;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _eventsSubscribed = false;
    }

    private void UpdateTitle()
    {
        if (_viewModel != null)
        {
            Title = _viewModel.IsNew ? "New Task" : "Edit Task";
        }
    }
}
