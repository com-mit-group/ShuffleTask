using System;

namespace ShuffleTask.Tests.TestDoubles;

public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FakeTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void AdvanceTime(TimeSpan amount)
    {
        _utcNow = _utcNow.Add(amount);
    }

    public void SetUtcNow(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }
}
