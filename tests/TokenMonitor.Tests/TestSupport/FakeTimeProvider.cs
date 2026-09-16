namespace TokenMonitor.Tests.TestSupport;

internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FakeTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    public DateTimeOffset Now
    {
        get => _now;
        set => _now = value;
    }

    public override DateTimeOffset GetUtcNow() => _now;
}
