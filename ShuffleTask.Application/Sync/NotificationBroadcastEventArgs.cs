using System;
using ShuffleTask.Domain.Events;

namespace ShuffleTask.Application.Sync;

public sealed class NotificationBroadcastEventArgs : EventArgs
{
    public NotificationBroadcastEventArgs(NotificationBroadcasted notification, bool isRemote)
    {
        Notification = notification ?? throw new ArgumentNullException(nameof(notification));
        IsRemote = isRemote;
    }

    public NotificationBroadcasted Notification { get; }

    public bool IsRemote { get; }
}
