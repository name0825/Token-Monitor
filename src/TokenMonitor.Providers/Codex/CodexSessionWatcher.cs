namespace TokenMonitor.Providers.Codex;

public sealed class CodexSessionWatcher : IDisposable
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1);
    private readonly string _sessionsDirectory;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private Timer? _retryTimer;
    private bool _disposed;

    public CodexSessionWatcher(string sessionsDirectory, TimeSpan debounce)
    {
        if (string.IsNullOrWhiteSpace(sessionsDirectory))
        {
            throw new ArgumentException("Sessions directory must be provided", nameof(sessionsDirectory));
        }

        if (debounce < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(debounce));
        }

        _sessionsDirectory = sessionsDirectory;
        _debounce = debounce;
    }

    public CodexSessionWatcher(string sessionsDirectory) : this(sessionsDirectory, TimeSpan.FromSeconds(2))
    {
    }

    public bool IsActive { get; private set; }

    public event EventHandler? Changed;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _watcher is not null || _retryTimer is not null)
            {
                return;
            }

            if (!TryStartWatcher())
            {
                ScheduleRetry();
            }
        }
    }

    private bool TryStartWatcher()
    {
        if (!Directory.Exists(_sessionsDirectory))
        {
            return false;
        }

        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(_sessionsDirectory, "rollout-*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };

            watcher.Created += OnFileSystemEvent;
            watcher.Changed += OnFileSystemEvent;
            watcher.Renamed += OnFileSystemEvent;
            watcher.Error += OnError;

            watcher.EnableRaisingEvents = true;

            _watcher = watcher;
            _timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
            IsActive = true;
            return true;
        }
        catch (IOException)
        {
            watcher?.Dispose();
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            watcher?.Dispose();
            return false;
        }
        catch (ArgumentException)
        {
            watcher?.Dispose();
            return false;
        }
    }

    private void ScheduleRetry()
    {
        _retryTimer ??= new Timer(OnRetryTimer, null, RetryInterval, Timeout.InfiniteTimeSpan);
    }

    private void OnRetryTimer(object? state)
    {
        var started = false;

        lock (_gate)
        {
            if (_disposed || _watcher is not null)
            {
                return;
            }

            if (TryStartWatcher())
            {
                _retryTimer?.Dispose();
                _retryTimer = null;
                started = true;
            }
            else
            {
                _retryTimer?.Change(RetryInterval, Timeout.InfiniteTimeSpan);
            }
        }

        if (started)
        {
            RaiseChanged();
        }
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        ScheduleRaise();
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        ScheduleRaise();
    }

    private void ScheduleRaise()
    {
        if (_debounce == TimeSpan.Zero)
        {
            RaiseChanged();
            return;
        }

        lock (_gate)
        {
            _timer?.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnTimer(object? state)
    {
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileSystemEvent;
                _watcher.Changed -= OnFileSystemEvent;
                _watcher.Renamed -= OnFileSystemEvent;
                _watcher.Error -= OnError;
                _watcher.Dispose();
                _watcher = null;
            }

            _timer?.Dispose();
            _timer = null;
            _retryTimer?.Dispose();
            _retryTimer = null;
            IsActive = false;
        }
    }
}
