using ShuffleTask.Models;

namespace ShuffleTask.Services;

public interface ISchedulerService
{
    TimeSpan NextGap(AppSettings settings, DateTimeOffset now);

    TaskItem? PickNextTask(IEnumerable<TaskItem> tasks, AppSettings settings, DateTimeOffset now);
}
