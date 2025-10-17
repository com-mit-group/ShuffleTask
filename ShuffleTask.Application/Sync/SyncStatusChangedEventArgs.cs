using System;

namespace ShuffleTask.Application.Sync;

public sealed class SyncStatusChangedEventArgs : EventArgs
{
    public SyncStatusChangedEventArgs(bool isConnected, Exception? error)
    {
        IsConnected = isConnected;
        Error = error;
    }

    public bool IsConnected { get; }

    public Exception? Error { get; }
}
