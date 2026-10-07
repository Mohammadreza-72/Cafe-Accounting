using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CafeArian.Services;

public static class WindowLayout
{
    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) => Fit(window);
        window.DpiChanged += (_, _) => window.Dispatcher.BeginInvoke(new Action(() => Fit(window)));
    }

    private static void Fit(Window window)
    {
        if (window.WindowState != WindowState.Normal) return;
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = MonitorFromWindow(handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;
        var dpi = VisualTreeHelper.GetDpi(window);
        var work = new Rect(info.Work.Left / dpi.DpiScaleX, info.Work.Top / dpi.DpiScaleY,
            (info.Work.Right - info.Work.Left) / dpi.DpiScaleX,
            (info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY);
        FitToWorkArea(window, work);
    }

    // The monitor work area excludes the taskbar and is converted to WPF units.
    internal static void FitToWorkArea(Window window, Rect work)
    {
        if (work.Width <= 0 || work.Height <= 0) return;
        window.MinWidth = Math.Min(window.MinWidth, work.Width);
        window.MinHeight = Math.Min(window.MinHeight, work.Height);
        window.Width = Math.Min(window.Width, work.Width);
        window.Height = Math.Min(window.Height, work.Height);
        if (!double.IsNaN(window.Left))
            window.Left = Math.Clamp(window.Left, work.Left, work.Right - window.Width);
        if (!double.IsNaN(window.Top))
            window.Top = Math.Clamp(window.Top, work.Top, work.Bottom - window.Height);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
    }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
