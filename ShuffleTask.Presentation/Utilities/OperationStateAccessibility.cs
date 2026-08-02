using Microsoft.Maui.Accessibility;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using ShuffleTask.Presentation.Models;

namespace ShuffleTask.Presentation.Utilities;

internal static class OperationStateAccessibility
{
    public static void Announce(IDispatcher dispatcher, VisualElement? focusTarget, OperationState state)
    {
        if (string.IsNullOrWhiteSpace(state.Announcement))
        {
            return;
        }

        dispatcher.Dispatch(() =>
        {
            if (state.IsBlocking)
            {
                focusTarget?.SetSemanticFocus();
            }

            SemanticScreenReader.Default.Announce(state.Announcement);
        });
    }
}
