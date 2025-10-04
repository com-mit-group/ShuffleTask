using System;
using System.Threading.Tasks;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Presentation.Utilities;

public static class CutInLineUtilities
{
    public static async Task ClearCutInLineOnceAsync(TaskItem task, IStorageService storage)
    {
        if (task is null)
        {
            throw new ArgumentNullException(nameof(task));
        }

        if (storage is null)
        {
            throw new ArgumentNullException(nameof(storage));
        }

        if (task.CutInLineMode != CutInLineMode.Once)
        {
            return;
        }

        task.CutInLineMode = CutInLineMode.None;
        await storage.UpdateTaskAsync(task).ConfigureAwait(false);
    }
}
