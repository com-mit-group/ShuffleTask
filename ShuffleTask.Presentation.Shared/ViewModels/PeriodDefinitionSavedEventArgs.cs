using ShuffleTask.Domain.Entities;

namespace ShuffleTask.ViewModels;

// Shared event payload emitted by reusable period definition editor state.
public sealed class PeriodDefinitionSavedEventArgs : EventArgs
{
    public PeriodDefinitionSavedEventArgs(PeriodDefinition definition)
    {
        Definition = definition;
    }

    public PeriodDefinition Definition { get; }
}
