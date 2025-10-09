using System;
using System.Threading;
using System.Threading.Tasks;

namespace ShuffleTask.Presentation.Services;

public interface IPersistentBackgroundService
{
    Task InitializeAsync();

    Task ScheduleAsync(TimeSpan delay, CancellationToken cancellationToken, Func<Task> callback);

    void Schedule(DateTimeOffset when, string? taskId);

    void Cancel();
}
