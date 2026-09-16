namespace TokenMonitor.Providers.Cli;

internal sealed class ConPtyCliSession : ICliSession
{
    private const int Columns = 120;
    private const int Rows = 200;

    private readonly ConPtySession _session;

    private ConPtyCliSession(ConPtySession session)
    {
        _session = session;
    }

    public static ConPtyCliSession Start(string commandLine, string workingDirectory) =>
        new(ConPtySession.Start(commandLine, workingDirectory, Columns, Rows));

    public Task WriteAsync(string text, CancellationToken cancellationToken) =>
        _session.WriteAsync(text, cancellationToken);

    public Task WaitForIdleAsync(TimeSpan quietPeriod, TimeSpan hardTimeout, CancellationToken cancellationToken) =>
        _session.WaitForIdleAsync(quietPeriod, hardTimeout, cancellationToken);

    public string RenderScreen()
    {
        var screen = new TerminalScreen(Columns, Rows);
        screen.Write(_session.Snapshot());
        return screen.Render();
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();
}
