using System.ComponentModel;
using ShuffleTask.Presentation.Models;
using ShuffleTask.Presentation.Utilities;
using ShuffleTask.ViewModels;

namespace ShuffleTask.Views;

public partial class OnboardingPage : ContentPage
{
    private readonly OnboardingViewModel _viewModel;

    public OnboardingPage(OnboardingViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        _viewModel.OperationState.PropertyChanged += OnOperationStateChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250), () =>
        {
            if (_viewModel.CanChoose)
            {
                CreateTaskButton.Focus();
            }
        });
    }

    protected override bool OnBackButtonPressed() => _viewModel.IsVisible || base.OnBackButtonPressed();

    private void OnOperationStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationState.Announcement)
            && !string.IsNullOrWhiteSpace(_viewModel.OperationState.Announcement))
        {
            OperationStateAccessibility.Announce(Dispatcher, OperationStateMessage, _viewModel.OperationState);
        }

        if (e.PropertyName == nameof(OperationState.IsLoading) && _viewModel.CanChoose)
        {
            Dispatcher.Dispatch(() => CreateTaskButton.Focus());
        }
    }
}
