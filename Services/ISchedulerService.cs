using ShuffleTask.Models;

namespace ShuffleTask.Services;

public interface ISchedulerService
{
    TimeSpan NextGap(AppSettings settings, DateTime nowLocal);

    TaskItem? PickNextTask(IEnumerable<TaskItem> tasks, AppSettings settings, DateTime nowLocal);
}
