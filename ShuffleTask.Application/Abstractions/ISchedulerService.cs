using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Abstractions;

public interface ISchedulerService
{
    TimeSpan NextGap(AppSettings settings, DateTimeOffset now);

    TaskItem? PickNextTask(IEnumerable<TaskItem> tasks, AppSettings settings, DateTimeOffset now);
}
