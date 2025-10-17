using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ShuffleTask.Application.Sync;

public sealed class TasksChangedEventArgs : EventArgs
{
    public TasksChangedEventArgs(IEnumerable<string> taskIds, bool isRemote, string? originDeviceId)
    {
        TaskIds = new ReadOnlyCollection<string>(taskIds?.ToArray() ?? Array.Empty<string>());
        IsRemote = isRemote;
        OriginDeviceId = originDeviceId;
    }

    public IReadOnlyList<string> TaskIds { get; }

    public bool IsRemote { get; }

    public string? OriginDeviceId { get; }
}
