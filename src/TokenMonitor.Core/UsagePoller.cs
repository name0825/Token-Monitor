namespace TokenMonitor.Core;

public sealed class UsagePoller : IAsyncDisposable
{
    private readonly IUsageProvider _provider;
    private readonly TimeProvider _timeProvider;
    private readonly PollingSchedule _schedule;
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    private Task? _loopTask;
    private int _started;
    private bool _disposed;

    public UsagePoller(IUsageProvider provider, PollingOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _provider = provider;
        _timeProvider = timeProvider;
        _schedule = new PollingSchedule(options);
    }

    public UsagePoller(IUsageProvider provider, PollingOptions options)
        : this(provider, options, TimeProvider.System)
    {
    }

    public Tool Tool => _provider.Tool;

    public UsageResult? Latest { get; private set; }

    public DateTimeOffset? LatestAt { get; private set; }

    public event EventHandler<UsageResult>? Updated;

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            return;
        }

        _loopTask = RunLoopAsync(_cts.Token);
    }

    public async Task<UsageResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        var result = await PollAsync(linkedCts.Token).ConfigureAwait(false);
        _schedule.Next(result);
        return result;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await PollAsync(cancellationToken).ConfigureAwait(false);
                var delay = _schedule.Next(result);

                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<UsageResult> PollAsync(CancellationToken cancellationToken)
    {
        await _pollLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (_disposed)
        {
            _pollLock.Release();
            throw new ObjectDisposedException(GetType().FullName);
        }

        UsageResult result;
        try
        {
            result = await _provider.GetUsageAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = UsageResult.Failure(UsageFailureKind.Unavailable, ex.Message);
        }
        finally
        {
            _pollLock.Release();
        }

        Latest = result;
        LatestAt = _timeProvider.GetUtcNow();
        RaiseUpdated(result);
        return result;
    }

    private void RaiseUpdated(UsageResult result)
    {
        try
        {
            Updated?.Invoke(this, result);
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _cts.Cancel();

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _pollLock.WaitAsync().ConfigureAwait(false);

        _cts.Dispose();
        _pollLock.Dispose();
    }
}
