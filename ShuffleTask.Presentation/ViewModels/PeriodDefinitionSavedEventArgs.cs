using ShuffleTask.Domain.Entities;

namespace ShuffleTask.ViewModels;

public sealed class PeriodDefinitionSavedEventArgs : EventArgs
{
    public PeriodDefinitionSavedEventArgs(PeriodDefinition definition)
    {
        Definition = definition;
    }

    public PeriodDefinition Definition { get; }
}
