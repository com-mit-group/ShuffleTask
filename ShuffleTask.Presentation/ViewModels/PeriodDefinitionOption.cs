using ShuffleTask.Domain.Entities;

namespace ShuffleTask.ViewModels;

public sealed class PeriodDefinitionOption
{
    public PeriodDefinitionOption(string id, string name, string description, PeriodDefinition? definition, bool isAdHoc, bool isCoreBuiltIn)
    {
        Id = id;
        Name = name;
        Description = description;
        Definition = definition;
        IsAdHoc = isAdHoc;
        IsCoreBuiltIn = isCoreBuiltIn;
    }

    public string Id { get; }

    public string Name { get; }

    public string Description { get; }

    public PeriodDefinition? Definition { get; }

    public bool IsAdHoc { get; }

    public bool IsCoreBuiltIn { get; }

    public bool IsEditable => !IsAdHoc && !IsCoreBuiltIn;
}
