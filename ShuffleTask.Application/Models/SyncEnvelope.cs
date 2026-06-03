namespace ShuffleTask.Application.Models;

public sealed class SyncEnvelope
{
    public Guid EnvelopeId { get; init; } = Guid.NewGuid();

    public SyncEnvelopeKind Kind { get; init; }

    public SyncManifest? Manifest { get; init; }

    public SyncTaskRequest? TaskRequest { get; init; }

    public SyncTaskBatch? TaskBatch { get; init; }

    public static SyncEnvelope ForManifest(SyncManifest manifest)
        => new() { Kind = SyncEnvelopeKind.Manifest, Manifest = manifest };

    public static SyncEnvelope ForTaskRequest(SyncTaskRequest request)
        => new() { Kind = SyncEnvelopeKind.TaskRequest, TaskRequest = request };

    public static SyncEnvelope ForTaskBatch(SyncTaskBatch batch)
        => new() { Kind = SyncEnvelopeKind.TaskBatch, TaskBatch = batch };
}

public enum SyncEnvelopeKind
{
    Manifest = 0,
    TaskRequest = 1,
    TaskBatch = 2,
}
