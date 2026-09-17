namespace TokenMonitor.Core;

public sealed class FallbackUsageProvider : IUsageProvider
{
    private readonly IUsageProvider _primary;
    private readonly IUsageProvider _fallback;
    private readonly TimeSpan? _maxPrimaryAge;
    private readonly TimeProvider? _timeProvider;

    public FallbackUsageProvider(IUsageProvider primary, IUsageProvider fallback)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(fallback);

        if (primary.Tool != fallback.Tool)
        {
            throw new ArgumentException("Primary and fallback providers must target the same tool.", nameof(fallback));
        }

        _primary = primary;
        _fallback = fallback;
    }

    public FallbackUsageProvider(IUsageProvider primary, IUsageProvider fallback, TimeSpan maxPrimaryAge, TimeProvider timeProvider)
        : this(primary, fallback)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (maxPrimaryAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPrimaryAge), "Maximum primary age must be positive.");
        }

        _maxPrimaryAge = maxPrimaryAge;
        _timeProvider = timeProvider;
    }

    public Tool Tool => _primary.Tool;

    public async Task<UsageResult> GetUsageAsync(CancellationToken cancellationToken)
    {
        var primaryResult = await GetResultAsync(_primary, cancellationToken).ConfigureAwait(false);
        if (!primaryResult.IsSuccess)
        {
            var fallbackResult = await GetResultAsync(_fallback, cancellationToken).ConfigureAwait(false);
            return fallbackResult.IsSuccess
                ? fallbackResult
                : UsageResult.Failure(primaryResult.FailureKind!.Value, primaryResult.Message ?? string.Empty, fallbackResult.Message);
        }

        if (_maxPrimaryAge is null || IsFresh(primaryResult))
        {
            return primaryResult;
        }

        var staleFallbackResult = await GetResultAsync(_fallback, cancellationToken).ConfigureAwait(false);
        if (!staleFallbackResult.IsSuccess)
        {
            return primaryResult;
        }

        DateTimeOffset? primaryNewest = NewestObservedAt(primaryResult);
        DateTimeOffset? fallbackNewest = NewestObservedAt(staleFallbackResult);
        return fallbackNewest.HasValue && (!primaryNewest.HasValue || fallbackNewest.Value > primaryNewest.Value)
            ? staleFallbackResult
            : primaryResult;
    }

    private bool IsFresh(UsageResult result)
    {
        DateTimeOffset? newest = NewestObservedAt(result);
        return newest.HasValue && newest.Value >= _timeProvider!.GetUtcNow() - _maxPrimaryAge!.Value;
    }

    private static DateTimeOffset? NewestObservedAt(UsageResult result) =>
        result.Snapshots is { Count: > 0 } snapshots ? snapshots.Max(s => s.ObservedAt) : null;

    private static async Task<UsageResult> GetResultAsync(IUsageProvider provider, CancellationToken cancellationToken)
    {
        try
        {
            return await provider.GetUsageAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return UsageResult.Failure(UsageFailureKind.Unavailable, ex.Message);
        }
    }
}
