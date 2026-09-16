using TokenMonitor.Providers.Cli;

namespace TokenMonitor.Tests.TestSupport;

internal sealed class FakeCliSession : ICliSession
{
    private readonly string _startupScreen;
    private readonly string _finalScreen;
    private int _renderCalls;

    public FakeCliSession(string startupScreen, string finalScreen)
    {
        _startupScreen = startupScreen;
        _finalScreen = finalScreen;
    }

    public List<string> Writes { get; } = new();

    public bool Disposed { get; private set; }

    public Func<CancellationToken, Task>? OnWaitForIdle { get; set; }

    public Exception? WriteException { get; set; }

    public Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        if (WriteException is not null)
        {
            throw WriteException;
        }

        Writes.Add(text);
        return Task.CompletedTask;
    }

    public async Task WaitForIdleAsync(TimeSpan quietPeriod, TimeSpan hardTimeout, CancellationToken cancellationToken)
    {
        if (OnWaitForIdle is not null)
        {
            await OnWaitForIdle(cancellationToken).ConfigureAwait(false);
        }
    }

    public string RenderScreen() => _renderCalls++ == 0 ? _startupScreen : _finalScreen;

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
