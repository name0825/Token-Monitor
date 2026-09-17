using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using H.NotifyIcon;
using TokenMonitor.App.Settings;
using TokenMonitor.App.ViewModels;
using TokenMonitor.Core;
using TokenMonitor.Providers.Claude;
using TokenMonitor.Providers.Cli;
using TokenMonitor.Providers.Codex;

namespace TokenMonitor.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\TokenMonitor.SingleInstance";
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(3);

    private readonly SettingsStore _settingsStore = new();
    private readonly OverlayViewModel _viewModel = new();

    private OverlaySettings _settings = new();
    private HttpClient? _httpClient;
    private UsagePoller? _claudePoller;
    private UsagePoller? _codexPoller;
    private CodexSessionWatcher? _codexWatcher;
    private TaskbarIcon? _trayIcon;
    private OverlayWindow? _window;
    private DispatcherTimer? _tickTimer;
    private Mutex? _singleInstanceMutex;
    private int _codexRefreshing;
    private Task _claudeRefreshTask = Task.CompletedTask;
    private Task _codexRefreshTask = Task.CompletedTask;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Mutex singleInstanceMutex = new(true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            singleInstanceMutex.Dispose();
            Shutdown();
            return;
        }

        _singleInstanceMutex = singleInstanceMutex;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _settings = _settingsStore.Load();

        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        string sessionsDirectory = CodexLogProvider.ResolveDefaultSessionsDirectory();
        PollingOptions options = new()
        {
            Interval = TimeSpan.FromSeconds(_settings.PollingIntervalSeconds),
            MinimumInterval = TimeSpan.FromSeconds(3),
        };

        _claudePoller = new UsagePoller(
            new FallbackUsageProvider(
                new ClaudeOAuthProvider(_httpClient, ClaudeOAuthProvider.ResolveDefaultCredentialsPath(), TimeProvider.System),
                new CliScrapeProvider(Tool.Claude)),
            options);
        _codexPoller = new UsagePoller(
            new FallbackUsageProvider(
                new CodexLogProvider(sessionsDirectory),
                new CliScrapeProvider(Tool.Codex),
                TimeSpan.FromMinutes(10),
                TimeProvider.System),
            options);

        _claudePoller.Updated += OnPollerUpdated;
        _codexPoller.Updated += OnPollerUpdated;

        _codexWatcher = new CodexSessionWatcher(sessionsDirectory);
        _codexWatcher.Changed += OnCodexSessionsChanged;
        _codexWatcher.Start();

        _window = new OverlayWindow(_viewModel, _settings);
        _window.PositionChanged += OnWindowPositionChanged;
        _window.Show();

        _trayIcon = CreateTrayIcon();

        _tickTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _tickTimer.Tick += (_, _) => _viewModel.Tick(DateTimeOffset.Now);
        _tickTimer.Start();

        if (_settings.ShowClaude)
        {
            _claudePoller.Start();
        }

        if (_settings.ShowCodex)
        {
            _codexPoller.Start();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_singleInstanceMutex is null)
        {
            base.OnExit(e);
            return;
        }

        _settingsStore.Save(_settings);

        _tickTimer?.Stop();

        if (_codexWatcher is not null)
        {
            _codexWatcher.Changed -= OnCodexSessionsChanged;
            _codexWatcher.Dispose();
        }

        if (_claudePoller is not null)
        {
            _claudePoller.Updated -= OnPollerUpdated;
        }

        if (_codexPoller is not null)
        {
            _codexPoller.Updated -= OnPollerUpdated;
        }

        try
        {
            Task.WhenAll(
                DisposePollerAsync(_claudePoller),
                DisposePollerAsync(_codexPoller),
                _claudeRefreshTask,
                _codexRefreshTask).Wait(ExitTimeout);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }

        _trayIcon?.Dispose();
        _httpClient?.Dispose();

        DispatcherUnhandledException -= OnDispatcherUnhandledException;

        _singleInstanceMutex.ReleaseMutex();
        _singleInstanceMutex.Dispose();
        _singleInstanceMutex = null;

        base.OnExit(e);
    }

    private static Task DisposePollerAsync(UsagePoller? poller)
    {
        if (poller is null)
        {
            return Task.CompletedTask;
        }

        return Task.Run(async () => await poller.DisposeAsync().ConfigureAwait(false));
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine(e.Exception.GetType().FullName);
        e.Handled = true;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine(e.Exception.GetType().FullName);
        e.SetObserved();
    }

    private void OnPollerUpdated(object? sender, UsageResult result)
    {
        if (sender is not UsagePoller poller)
        {
            return;
        }

        Tool tool = poller.Tool;
        Dispatcher.BeginInvoke(() => _viewModel.Apply(tool, result, DateTimeOffset.Now));
    }

    private void OnCodexSessionsChanged(object? sender, EventArgs e)
    {
        if (!_settings.ShowCodex || _codexPoller is null || Interlocked.CompareExchange(ref _codexRefreshing, 1, 0) != 0)
        {
            return;
        }

        _codexRefreshTask = RefreshCodexAsync();
    }

    private async Task RefreshCodexAsync()
    {
        try
        {
            if (_codexPoller is not null)
            {
                await _codexPoller.RefreshAsync().ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            Interlocked.Exchange(ref _codexRefreshing, 0);
        }
    }

    private void OnWindowPositionChanged(object? sender, (double Left, double Top) position)
    {
        _settings = _settings with { Left = position.Left, Top = position.Top };
        _settingsStore.Save(_settings);
    }

    private TaskbarIcon CreateTrayIcon()
    {
        ContextMenu menu = new();

        MenuItem refresh = new() { Header = "지금 새로고침" };
        refresh.Click += (_, _) => RefreshAll();
        menu.Items.Add(refresh);
        menu.Items.Add(new Separator());

        menu.Items.Add(CreateToggle("클릭 통과", _settings.ClickThrough, value =>
        {
            _settings = _settings with { ClickThrough = value };
            _window?.ApplySettings(_settings);
        }));

        menu.Items.Add(CreateToggle("항상 위", _settings.AlwaysOnTop, value =>
        {
            _settings = _settings with { AlwaysOnTop = value };
            _window?.ApplySettings(_settings);
        }));

        menu.Items.Add(CreateToggle("Claude 표시", _settings.ShowClaude, value =>
        {
            _settings = _settings with { ShowClaude = value };
            _window?.ApplySettings(_settings);
            if (value)
            {
                _claudePoller?.Start();
            }
            else
            {
                _claudePoller?.Stop();
            }
        }));

        menu.Items.Add(CreateToggle("Codex 표시", _settings.ShowCodex, value =>
        {
            _settings = _settings with { ShowCodex = value };
            _window?.ApplySettings(_settings);
            if (value)
            {
                _codexPoller?.Start();
            }
            else
            {
                _codexPoller?.Stop();
            }
        }));

        menu.Items.Add(CreateIntervalMenu());

        MenuItem autoStart = new()
        {
            Header = "Windows 시작 시 실행",
            IsCheckable = true,
            IsChecked = AutoStartManager.IsEnabled(),
        };
        autoStart.Click += (_, _) =>
        {
            AutoStartManager.SetEnabled(autoStart.IsChecked);
            autoStart.IsChecked = AutoStartManager.IsEnabled();
        };
        menu.Items.Add(autoStart);

        menu.Items.Add(new Separator());

        MenuItem exit = new() { Header = "종료" };
        exit.Click += (_, _) => Current.Shutdown();
        menu.Items.Add(exit);

        TaskbarIcon icon = new()
        {
            ToolTipText = "Token Monitor",
            ContextMenu = menu,
            Icon = RasterizeTrayIcon((ImageSource)Resources["TrayIconImage"]),
        };
        icon.ForceCreate();
        return icon;
    }

    private static System.Drawing.Icon RasterizeTrayIcon(ImageSource source)
    {
        const int size = 32;
        const int headerLength = 22;

        DrawingVisual visual = new();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawImage(source, new Rect(0, 0, size, size));
        }

        RenderTargetBitmap bitmap = new(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using MemoryStream png = new();
        encoder.Save(png);
        byte[] pngBytes = png.ToArray();

        using MemoryStream ico = new();
        BinaryWriter writer = new(ico);
        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write((byte)size);
        writer.Write((byte)size);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(pngBytes.Length);
        writer.Write(headerLength);
        writer.Write(pngBytes);
        writer.Flush();
        ico.Position = 0;

        return new System.Drawing.Icon(ico);
    }

    private static readonly (string Label, int Seconds)[] PollingIntervalOptions =
    [
        ("3초", 3),
        ("5초", 5),
        ("10초", 10),
        ("15초", 15),
        ("30초", 30),
        ("1분", 60),
        ("3분", 180),
        ("5분", 300),
        ("10분", 600),
        ("15분", 900),
        ("30분", 1800),
    ];

    private const int PollingIntervalSecondsOptionsCount = 5;

    private MenuItem CreateIntervalMenu()
    {
        MenuItem intervalMenu = new() { Header = "업데이트 주기" };
        List<MenuItem> items = new();

        for (int i = 0; i < PollingIntervalOptions.Length; i++)
        {
            if (i == PollingIntervalSecondsOptionsCount)
            {
                intervalMenu.Items.Add(new Separator());
            }

            var (label, seconds) = PollingIntervalOptions[i];
            MenuItem item = new()
            {
                Header = label,
                IsCheckable = true,
                IsChecked = _settings.PollingIntervalSeconds == seconds,
            };

            item.Click += (_, _) =>
            {
                _settings = _settings with { PollingIntervalSeconds = seconds };
                foreach (MenuItem other in items)
                {
                    other.IsChecked = ReferenceEquals(other, item);
                }

                TimeSpan interval = TimeSpan.FromSeconds(seconds);
                _claudePoller?.UpdateInterval(interval);
                _codexPoller?.UpdateInterval(interval);
                _settingsStore.Save(_settings);
            };

            items.Add(item);
            intervalMenu.Items.Add(item);
        }

        return intervalMenu;
    }

    private MenuItem CreateToggle(string header, bool isChecked, Action<bool> apply)
    {
        MenuItem item = new()
        {
            Header = header,
            IsCheckable = true,
            IsChecked = isChecked,
        };

        item.Click += (_, _) =>
        {
            apply(item.IsChecked);
            _settingsStore.Save(_settings);
        };

        return item;
    }

    private void RefreshAll()
    {
        if (_settings.ShowClaude)
        {
            _claudeRefreshTask = RefreshPollerAsync(_claudePoller);
        }

        if (_settings.ShowCodex)
        {
            OnCodexSessionsChanged(this, EventArgs.Empty);
        }
    }

    private static async Task RefreshPollerAsync(UsagePoller? poller)
    {
        if (poller is null)
        {
            return;
        }

        try
        {
            await poller.RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }
}
