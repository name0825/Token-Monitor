using System.Runtime.InteropServices;

namespace TokenMonitor.App.Interop;

internal static class NativeMethods
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

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
