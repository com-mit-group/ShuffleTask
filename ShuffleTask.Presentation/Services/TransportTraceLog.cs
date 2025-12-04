using System.Collections.Concurrent;
using System.Threading.Channels;
using Yaref92.Events;

namespace ShuffleTask.Presentation.Services;

public sealed class TransportTraceLog
{
    private readonly ConcurrentQueue<TransportConnectionTrace> _connections = new();
    private readonly ConcurrentQueue<TransportEventTrace> _events = new();
    private readonly Channel<TransportEventTrace> _inboundChannel = Channel.CreateUnbounded<TransportEventTrace>();

    public IReadOnlyCollection<TransportConnectionTrace> Connections => _connections.ToArray();

    public IReadOnlyCollection<TransportEventTrace> SentEvents
        => _events.Where(e => e.Direction == TransportEventDirection.Sent).ToArray();

    public IReadOnlyCollection<TransportEventTrace> ReceivedEvents
        => _events.Where(e => e.Direction == TransportEventDirection.Received).ToArray();

    public void RecordConnection(string status, string? details = null)
    {
        _connections.Enqueue(new TransportConnectionTrace(status, DateTimeOffset.UtcNow, details));
    }

    public void RecordSend(DomainEventBase domainEvent, string? deviceId)
    {
        _events.Enqueue(TransportEventTrace.Create(TransportEventDirection.Sent, domainEvent, deviceId));
    }

    public void RecordReceive(DomainEventBase domainEvent, string? deviceId)
    {
        var trace = TransportEventTrace.Create(TransportEventDirection.Received, domainEvent, deviceId);
        _events.Enqueue(trace);
        _inboundChannel.Writer.TryWrite(trace);
    }

    public async Task<TransportEventTrace?> WaitForReceiveAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            return await _inboundChannel.Reader.ReadAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}

public enum TransportEventDirection
{
    Sent,
    Received
}

public sealed record TransportConnectionTrace(string Status, DateTimeOffset Timestamp, string? Details);

public sealed record TransportEventTrace(
    TransportEventDirection Direction,
    string EventType,
    string? DeviceId,
    DateTimeOffset Timestamp,
    string? Details)
{
    public static TransportEventTrace Create(TransportEventDirection direction, DomainEventBase domainEvent, string? deviceId)
    {
        string eventType = domainEvent.GetType().Name;
        string details = domainEvent.GetType().FullName ?? eventType;
        return new TransportEventTrace(direction, eventType, deviceId, DateTimeOffset.UtcNow, details);
    }
}
