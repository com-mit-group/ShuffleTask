using ShuffleTask.Domain.Entities;

namespace ShuffleTask.ViewModels;

public sealed class AlignmentModeOption
{
    public AlignmentModeOption(string name, string description, PeriodDefinitionMode mode)
    {
        Name = name;
        Description = description;
        Mode = mode;
    }

    public string Name { get; }

    public string Description { get; }

    public PeriodDefinitionMode Mode { get; }
}
