using ShuffleTask.Application.Models;

namespace ShuffleTask.Application.Abstractions;

public interface ISyncTransportAdapter
{
    event EventHandler<SyncEnvelopeReceivedEventArgs>? EnvelopeReceived;

    Task SendAsync(SyncEnvelope envelope, CancellationToken cancellationToken = default);
}

public sealed class SyncEnvelopeReceivedEventArgs(SyncEnvelope envelope) : EventArgs
{
    public SyncEnvelope Envelope { get; } = envelope ?? throw new ArgumentNullException(nameof(envelope));
}
