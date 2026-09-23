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

        Rect work = GetCurrentMonitorWorkArea();
        Left = Clamp(Left, work.Left, work.Right - ActualWidth);
        Top = Clamp(Top, work.Top, work.Bottom - ActualHeight);
    }

    private Rect GetCurrentMonitorWorkArea()
    {
        if (_handle != IntPtr.Zero
            && NativeMethods.TryGetWorkAreaForWindow(_handle, out int left, out int top, out int right, out int bottom)
            && TryConvertDeviceRectToDip(left, top, right, bottom, out Rect workArea))
        {
            return workArea;
        }

        return SystemParameters.WorkArea;
    }

    private bool TryGetMonitorWorkArea(Rect dipRect, out Rect workAreaDip)
    {
        workAreaDip = default;

        if (PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
        {
            return false;
        }

        Point topLeft = target.TransformToDevice.Transform(dipRect.TopLeft);
        Point bottomRight = target.TransformToDevice.Transform(dipRect.BottomRight);

        return NativeMethods.TryGetWorkAreaForRect(
                (int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y),
                (int)Math.Round(bottomRight.X), (int)Math.Round(bottomRight.Y),
                out int left, out int top, out int right, out int bottom)
            && TryConvertDeviceRectToDip(left, top, right, bottom, out workAreaDip);
    }

    private bool TryConvertDeviceRectToDip(int left, int top, int right, int bottom, out Rect dipRect)
    {
        dipRect = default;

        if (PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
        {
            return false;
        }

        Point topLeft = target.TransformFromDevice.Transform(new Point(left, top));
        Point bottomRight = target.TransformFromDevice.Transform(new Point(right, bottom));
        dipRect = new Rect(topLeft, bottomRight);
        return true;
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

        if (TryGetMonitorWorkArea(new Rect(left, top, width, height), out Rect monitorWork))
        {
            Left = Clamp(left, monitorWork.Left, monitorWork.Right - width);
            Top = Clamp(top, monitorWork.Top, monitorWork.Bottom - height);
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
