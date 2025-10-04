using ShuffleTask.Application.Abstractions;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Application.Utilities;

public static class CutInLineUtilities
{
    public static async Task<bool> ClearCutInLineOnceAsync(TaskItem task, IStorageService storage)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(storage);

        if (task.CutInLineMode != CutInLineMode.Once)
        {
            return false;
        }

        task.CutInLineMode = CutInLineMode.None;
        await storage.UpdateTaskAsync(task).ConfigureAwait(false);
        return true;
    }
}
