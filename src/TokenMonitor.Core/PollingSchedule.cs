namespace TokenMonitor.Core;

public sealed class PollingSchedule
{
    private readonly PollingOptions _options;
    private TimeSpan? _currentBackoff;

    public PollingSchedule(PollingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        CurrentDelay = EffectiveInterval;
    }

    public TimeSpan EffectiveInterval => TimeSpanMax(_options.Interval, _options.MinimumInterval);

    public TimeSpan CurrentDelay { get; private set; }

    public TimeSpan Next(UsageResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        TimeSpan delay;
        if (result.IsSuccess)
        {
            _currentBackoff = null;
            delay = EffectiveInterval;
        }
        else if (result.FailureKind == UsageFailureKind.RateLimited)
        {
            var backoff = _currentBackoff is { } previous
                ? TimeSpanMin(previous + previous, _options.MaximumBackoff)
                : TimeSpanMax(_options.InitialBackoff, EffectiveInterval);

            _currentBackoff = backoff;
            delay = TimeSpanMax(backoff, EffectiveInterval);
        }
        else
        {
            _currentBackoff = null;
            delay = EffectiveInterval;
        }

        CurrentDelay = delay;
        return delay;
    }

    public void Reset()
    {
        _currentBackoff = null;
        CurrentDelay = EffectiveInterval;
    }

    private static TimeSpan TimeSpanMax(TimeSpan a, TimeSpan b) => a >= b ? a : b;

    private static TimeSpan TimeSpanMin(TimeSpan a, TimeSpan b) => a <= b ? a : b;
}
