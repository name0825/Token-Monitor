using TokenMonitor.Core;

namespace TokenMonitor.Tests.TestSupport;

internal sealed class FakeUsageProvider : IUsageProvider
{
    private readonly Func<CancellationToken, Task<UsageResult>> _handler;

    public FakeUsageProvider(Tool tool, Func<CancellationToken, Task<UsageResult>> handler)
    {
        Tool = tool;
        _handler = handler;
    }

    public Tool Tool { get; }

    public Task<UsageResult> GetUsageAsync(CancellationToken cancellationToken) => _handler(cancellationToken);
}
