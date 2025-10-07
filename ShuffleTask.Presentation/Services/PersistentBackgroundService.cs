namespace ShuffleTask.Presentation.Services;

public partial class PersistentBackgroundService : IPersistentBackgroundService
{
    private readonly TimeProvider _clock;
    private readonly IPersistentBackgroundPlatform _platform;

    public PersistentBackgroundService(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _platform = CreatePlatform(clock);
    }

    public Task InitializeAsync()
        => _platform.InitializeAsync();

    public void Schedule(DateTimeOffset when, string? taskId)
    {
        TimeSpan delay = when - _clock.GetUtcNow();
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        _platform.Cancel();
        _platform.Schedule(when, delay, taskId);
    }

    public void Cancel()
        => _platform.Cancel();

    private IPersistentBackgroundPlatform CreatePlatform(TimeProvider clock)
    {
        IPersistentBackgroundPlatform platform = new NoOpPersistentBackgroundPlatform();
        InitializePlatform(clock, ref platform);
        return platform;
    }

    partial void InitializePlatform(TimeProvider clock, ref IPersistentBackgroundPlatform platform);

    private interface IPersistentBackgroundPlatform
    {
        Task InitializeAsync();

        void Schedule(DateTimeOffset when, TimeSpan delay, string? taskId);

        void Cancel();
    }

    private sealed class NoOpPersistentBackgroundPlatform : IPersistentBackgroundPlatform
    {
        public Task InitializeAsync() => Task.CompletedTask;

        public void Schedule(DateTimeOffset when, TimeSpan delay, string? taskId)
        {
        }

        public void Cancel()
        {
        }
    }
}
