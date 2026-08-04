using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Abstractions;

public interface IOnboardingService
{
    Task<int> GetCompletedVersionAsync();
    Task CompleteAsync(int version);
    Task CompleteWithSamplesAsync(IReadOnlyCollection<TaskItem> samples, int version);
}
