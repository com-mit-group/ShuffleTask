using System;
using ShuffleTask.Domain.Events;


namespace ShuffleTask.Application.Sync;

public sealed class ShuffleStateChangedEventArgs : EventArgs
{
    public ShuffleStateChangedEventArgs(ShuffleStateChanged state, bool isRemote)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        IsRemote = isRemote;
    }

    public ShuffleStateChanged State { get; }

    public bool IsRemote { get; }
}
