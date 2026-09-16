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
    private int _codexRefreshing;
    private Task _claudeRefreshTask = Task.CompletedTask;
    private Task _codexRefreshTask = Task.CompletedTask;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = _settingsStore.Load();

        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        string sessionsDirectory = CodexLogProvider.ResolveDefaultSessionsDirectory();
        PollingOptions options = new();

        _claudePoller = new UsagePoller(
            new FallbackUsageProvider(
                new ClaudeOAuthProvider(_httpClient, ClaudeOAuthProvider.ResolveDefaultCredentialsPath(), TimeProvider.System),
                new CliScrapeProvider(Tool.Claude)),
            options);
        _codexPoller = new UsagePoller(
            new FallbackUsageProvider(
                new CodexLogProvider(sessionsDirectory),
                new CliScrapeProvider(Tool.Codex)),
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

        _claudePoller.Start();
        _codexPoller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Task.WhenAll(_claudeRefreshTask, _codexRefreshTask).Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }

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

        DisposePoller(_claudePoller);
        DisposePoller(_codexPoller);

        _trayIcon?.Dispose();
        _httpClient?.Dispose();

        _settingsStore.Save(_settings);

        base.OnExit(e);
    }

    private static void DisposePoller(UsagePoller? poller)
    {
        if (poller is null)
        {
            return;
        }

        try
        {
            poller.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
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
        if (_codexPoller is null || Interlocked.CompareExchange(ref _codexRefreshing, 1, 0) != 0)
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
        }));

        menu.Items.Add(CreateToggle("Codex 표시", _settings.ShowCodex, value =>
        {
            _settings = _settings with { ShowCodex = value };
            _window?.ApplySettings(_settings);
        }));

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
        _claudeRefreshTask = RefreshPollerAsync(_claudePoller);
        OnCodexSessionsChanged(this, EventArgs.Empty);
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
