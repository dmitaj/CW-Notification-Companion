using System;
using System.Runtime.InteropServices;

namespace CWNotificationCompanion;

internal static class NativeMethods
{
    [DllImport("user32.dll")] internal static extern bool FlashWindow(IntPtr hwnd, bool bInvert);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    internal const uint SWP_NOSIZE               = 0x0001;
    internal const uint SWP_NOZORDER             = 0x0004;
    internal const uint SWP_NOACTIVATE           = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int    cbSize;
        public RECT   rcMonitor;
        public RECT   rcWork;
        public uint   dwFlags;

        public static MONITORINFO Create() =>
            new() { cbSize = Marshal.SizeOf<MONITORINFO>() };
    }
}
