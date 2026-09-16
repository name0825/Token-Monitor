using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TokenMonitor.App.Interop;
using TokenMonitor.App.Settings;
using TokenMonitor.App.ViewModels;

namespace TokenMonitor.App;

public partial class OverlayWindow : Window
{
    private static readonly Color PanelColor = Color.FromRgb(0x1A, 0x1A, 0x1A);
    private const double EdgeMargin = 16;

    private readonly DispatcherTimer _positionSaveTimer;
    private OverlaySettings _settings;
    private IntPtr _handle;
    private bool _placed;

    public OverlayWindow(OverlayViewModel viewModel, OverlaySettings settings)
    {
        InitializeComponent();

        _settings = settings;
        DataContext = viewModel;

        _positionSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _positionSaveTimer.Tick += OnPositionSaveTick;

        Loaded += OnLoaded;
    }

    public event EventHandler<(double Left, double Top)>? PositionChanged;

    public void ApplySettings(OverlaySettings settings)
    {
        _settings = settings;

        Topmost = settings.AlwaysOnTop;
        PanelBorder.Background = new SolidColorBrush(PanelColor) { Opacity = settings.Opacity };

        if (DataContext is OverlayViewModel viewModel)
        {
            viewModel.Claude.IsVisible = settings.ShowClaude;
            viewModel.Codex.IsVisible = settings.ShowCodex;
        }

        NativeMethods.SetClickThrough(_handle, settings.ClickThrough);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _handle = new WindowInteropHelper(this).Handle;
        NativeMethods.ExcludeFromAltTab(_handle);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (!_settings.ClickThrough && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);

        if (!_placed)
        {
            return;
        }

        _positionSaveTimer.Stop();
        _positionSaveTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _positionSaveTimer.Stop();
        _positionSaveTimer.Tick -= OnPositionSaveTick;
        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplySettings(_settings);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PlaceWindow));
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_placed)
        {
            return;
        }

        Rect work = SystemParameters.WorkArea;
        Left = Clamp(Left, work.Left, work.Right - ActualWidth);
        Top = Clamp(Top, work.Top, work.Bottom - ActualHeight);
    }

    private void OnPositionSaveTick(object? sender, EventArgs e)
    {
        _positionSaveTimer.Stop();
        PositionChanged?.Invoke(this, (Left, Top));
    }

    private void PlaceWindow()
    {
        double width = ActualWidth;
        double height = ActualHeight;

        if (_settings.Left is not { } left || _settings.Top is not { } top)
        {
            Rect work = SystemParameters.WorkArea;
            Left = Clamp(work.Right - width - EdgeMargin, work.Left, work.Right - width);
            Top = work.Top + EdgeMargin;
            _placed = true;
            return;
        }

        double virtualLeft = SystemParameters.VirtualScreenLeft;
        double virtualTop = SystemParameters.VirtualScreenTop;
        double virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        double virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        bool onScreen = left + width > virtualLeft && left < virtualRight
            && top + height > virtualTop && top < virtualBottom;

        if (onScreen)
        {
            Left = Clamp(left, virtualLeft, virtualRight - width);
            Top = Clamp(top, virtualTop, virtualBottom - height);
            _placed = true;
            return;
        }

        Rect fallback = SystemParameters.WorkArea;
        Left = Clamp(left, fallback.Left, fallback.Right - width);
        Top = Clamp(top, fallback.Top, fallback.Bottom - height);
        _placed = true;
    }

    private static double Clamp(double value, double min, double max) =>
        max < min ? min : Math.Clamp(value, min, max);
}
