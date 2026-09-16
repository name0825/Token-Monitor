namespace TokenMonitor.Core;

public sealed class FallbackUsageProvider : IUsageProvider
{
    private readonly IUsageProvider _primary;
    private readonly IUsageProvider _fallback;

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

    public Tool Tool => _primary.Tool;

    public async Task<UsageResult> GetUsageAsync(CancellationToken cancellationToken)
    {
        var primaryResult = await GetResultAsync(_primary, cancellationToken).ConfigureAwait(false);
        if (primaryResult.IsSuccess)
        {
            return primaryResult;
        }

        var fallbackResult = await GetResultAsync(_fallback, cancellationToken).ConfigureAwait(false);
        return fallbackResult.IsSuccess ? fallbackResult : primaryResult;
    }

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
