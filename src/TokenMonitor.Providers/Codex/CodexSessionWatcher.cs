namespace TokenMonitor.Providers.Codex;

public sealed class CodexSessionWatcher : IDisposable
{
    private readonly string _sessionsDirectory;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _timer;
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
            if (_disposed || _watcher is not null)
            {
                return;
            }

            if (!Directory.Exists(_sessionsDirectory))
            {
                return;
            }

            var watcher = new FileSystemWatcher(_sessionsDirectory, "rollout-*.jsonl")
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
            IsActive = false;
        }
    }
}
