namespace TokenMonitor.Core;

public interface IUsageProvider
{
    Tool Tool { get; }

    Task<UsageResult> GetUsageAsync(CancellationToken cancellationToken);
}
