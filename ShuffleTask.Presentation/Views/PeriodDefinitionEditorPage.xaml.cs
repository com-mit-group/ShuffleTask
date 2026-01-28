using ShuffleTask.ViewModels;

namespace ShuffleTask.Views;

public partial class PeriodDefinitionEditorPage : ContentPage
{
    private PeriodDefinitionEditorViewModel? _viewModel;
    private bool _eventsSubscribed;

    public PeriodDefinitionEditorPage(PeriodDefinitionEditorViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel = BindingContext as PeriodDefinitionEditorViewModel;
        SubscribeToViewModel();
        UpdateTitle();
    }

    protected override void OnBindingContextChanged()
    {
        UnsubscribeFromViewModel();
        base.OnBindingContextChanged();
        _viewModel = BindingContext as PeriodDefinitionEditorViewModel;
        SubscribeToViewModel();
        UpdateTitle();
    }

    private async void OnSaved(object? sender, PeriodDefinitionSavedEventArgs e)
    {
        UnsubscribeFromViewModel();
        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        UnsubscribeFromViewModel();
        await Navigation.PopModalAsync();
    }

    private void SubscribeToViewModel()
    {
        if (_viewModel == null || _eventsSubscribed)
        {
            return;
        }

        _viewModel.Saved += OnSaved;
        _eventsSubscribed = true;
    }

    private void UnsubscribeFromViewModel()
    {
        if (_viewModel == null || !_eventsSubscribed)
        {
            return;
        }

        _viewModel.Saved -= OnSaved;
        _eventsSubscribed = false;
    }

    private void UpdateTitle()
    {
        if (_viewModel != null)
        {
            Title = _viewModel.IsNew ? "New period definition" : "Edit period definition";
        }
    }
}
