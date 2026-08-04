using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using ShuffleTask.ViewModels;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Presentation;
using ShuffleTask.Presentation.Models;
using ShuffleTask.Presentation.Utilities;

namespace ShuffleTask.Views;

public partial class EditTaskPage : ContentPage
{
    private EditTaskViewModel? _viewModel;
    private bool _eventsSubscribed;

    internal bool WasSavedBeforeClosing { get; private set; }

    public EditTaskPage(EditTaskViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        WasSavedBeforeClosing = false;

        if (BindingContext is EditTaskViewModel vm)
        {
            _viewModel = vm;
            SubscribeToViewModel();
        }

        UpdateTitle();
        if (_viewModel?.IsNew == true)
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250), () => TitleEntry.Focus());
        }
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
        WasSavedBeforeClosing = true;
        UnsubscribeFromViewModel();
        await Navigation.PopAsync();
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        WasSavedBeforeClosing = false;
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

    private async void OnCreatePeriodDefinitionClicked(object sender, EventArgs e)
    {
        await OpenPeriodDefinitionEditorAsync(null);
    }

    private async void OnEditSelectedPeriodDefinitionClicked(object sender, EventArgs e)
    {
        if (_viewModel?.SelectedPeriodDefinition?.Definition is PeriodDefinition definition)
        {
            await OpenPeriodDefinitionEditorAsync(definition);
        }
    }

    private async Task OpenPeriodDefinitionEditorAsync(PeriodDefinition? definition)
    {
        if (_viewModel == null)
        {
            return;
        }

        var editorPage = MauiProgram.Services.GetRequiredService<PeriodDefinitionEditorPage>();
        var editorViewModel = MauiProgram.Services.GetRequiredService<PeriodDefinitionEditorViewModel>();
        await editorViewModel.LoadAsync(definition);

        void OnSaved(object? sender, PeriodDefinitionSavedEventArgs args)
        {
            editorViewModel.Saved -= OnSaved;
            _ = _viewModel.RefreshPeriodDefinitionsAsync(args.Definition.Id);
        }

        void OnEditorDisappearing(object? sender, EventArgs args)
        {
            editorViewModel.Saved -= OnSaved;
            editorPage.Disappearing -= OnEditorDisappearing;
        }

        editorViewModel.Saved += OnSaved;
        editorPage.BindingContext = editorViewModel;
        editorPage.Disappearing -= OnEditorDisappearing;
        editorPage.Disappearing += OnEditorDisappearing;
        await Navigation.PushModalAsync(new NavigationPage(editorPage));
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
        _viewModel.OperationState.PropertyChanged += OnOperationStateChanged;
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
        _viewModel.OperationState.PropertyChanged -= OnOperationStateChanged;
        _eventsSubscribed = false;
    }

    private void OnOperationStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel == null
            || e.PropertyName != nameof(OperationState.Announcement)
            || string.IsNullOrWhiteSpace(_viewModel.OperationState.Announcement))
        {
            return;
        }

        OperationStateAccessibility.Announce(Dispatcher, OperationStateMessage, _viewModel.OperationState);
    }

    private void UpdateTitle()
    {
        if (_viewModel != null)
        {
            Title = _viewModel.IsNew ? "New Task" : "Edit Task";
        }
    }
}
