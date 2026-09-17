using System.Runtime.InteropServices;

namespace TokenMonitor.App.Interop;

internal static class NativeMethods
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "MonitorFromWindow")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    public static void ExcludeFromAltTab(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        long style = GetExtendedStyle(hwnd);
        SetExtendedStyle(hwnd, style | WS_EX_TOOLWINDOW);
    }

    public static void SetClickThrough(IntPtr hwnd, bool enabled)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        long style = GetExtendedStyle(hwnd);
        long updated = enabled
            ? style | WS_EX_LAYERED | WS_EX_TRANSPARENT
            : style & ~WS_EX_TRANSPARENT;

        SetExtendedStyle(hwnd, updated);
    }

    public static bool TryGetWorkAreaForWindow(IntPtr hwnd, out int left, out int top, out int right, out int bottom)
    {
        left = top = right = bottom = 0;

        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        left = info.rcWork.Left;
        top = info.rcWork.Top;
        right = info.rcWork.Right;
        bottom = info.rcWork.Bottom;
        return true;
    }

    private static long GetExtendedStyle(IntPtr hwnd) => Environment.Is64BitProcess
        ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64()
        : GetWindowLong32(hwnd, GWL_EXSTYLE);

    private static void SetExtendedStyle(IntPtr hwnd, long value)
    {
        if (Environment.Is64BitProcess)
        {
            SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(value));
        }
        else
        {
            SetWindowLong32(hwnd, GWL_EXSTYLE, unchecked((int)value));
        }
    }
}
