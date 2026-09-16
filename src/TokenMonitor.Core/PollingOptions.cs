namespace TokenMonitor.Core;

public sealed record PollingOptions
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(3);
    public TimeSpan MinimumInterval { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan MaximumBackoff { get; init; } = TimeSpan.FromMinutes(30);

    public void Validate()
    {
        if (Interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Interval), Interval, "Interval must be positive.");
        }

        if (MinimumInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumInterval), MinimumInterval, "MinimumInterval must be positive.");
        }

        if (InitialBackoff <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(InitialBackoff), InitialBackoff, "InitialBackoff must be positive.");
        }

        if (MaximumBackoff < InitialBackoff)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumBackoff), MaximumBackoff, "MaximumBackoff must be at least InitialBackoff.");
        }
    }
}
