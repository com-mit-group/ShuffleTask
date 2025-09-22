using System;
using System.ComponentModel;
using Microsoft.Maui.Controls;
using ShuffleTask.ViewModels;

namespace ShuffleTask.Views;

public partial class EditTaskPage : ContentPage
{
    private EditTaskViewModel? _viewModel;

    public EditTaskPage(EditTaskViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnBindingContextChanged()
    {
        if (_viewModel != null)
        {
            _viewModel.Saved -= OnSaved;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        base.OnBindingContextChanged();

        _viewModel = BindingContext as EditTaskViewModel;
        if (_viewModel != null)
        {
            _viewModel.Saved += OnSaved;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Title = _viewModel.IsNew ? "New Task" : "Edit Task";
        }
    }

    private async void OnSaved(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.Saved -= OnSaved;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        await Navigation.PopAsync();
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.Saved -= OnSaved;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
        await Navigation.PopAsync();
    }

    private void OnClearDeadline(object sender, EventArgs e)
    {
        _viewModel?.ResetDeadline();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditTaskViewModel.IsNew) && _viewModel != null)
        {
            Title = _viewModel.IsNew ? "New Task" : "Edit Task";
        }
    }
}
