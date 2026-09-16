namespace TokenMonitor.Providers.Cli;

public interface ICliSession : IAsyncDisposable
{
    Task WriteAsync(string text, CancellationToken cancellationToken);

    Task WaitForIdleAsync(TimeSpan quietPeriod, TimeSpan hardTimeout, CancellationToken cancellationToken);

    string RenderScreen();
}
