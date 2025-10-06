using System;
using System.Threading.Tasks;

namespace ShuffleTask.Presentation.Services;

public interface IPersistentBackgroundService
{
    Task InitializeAsync();

    void Schedule(DateTimeOffset when, string? taskId);

    void Cancel();
}
