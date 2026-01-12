using System;

namespace ShuffleTask.Application.Exceptions;

public class NetworkConnectionException : Exception
{
    public NetworkConnectionException(string message)
        : base(message)
    {
    }

    public NetworkConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
