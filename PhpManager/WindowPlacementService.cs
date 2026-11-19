using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace PhpManager;

/// <summary>
/// Remembers the main window's position, size and maximized state, including which monitor it was on, in
/// window.json next to the exe. It uses the Win32 window placement API, which works in physical pixels across
/// monitors with different scaling and remembers the normal bounds of a maximized window.
/// </summary>
public static class WindowPlacementService
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "window.json");

    public static void Attach(Window window)
    {
        var saved = Load();

        // A saved position that is still on a connected monitor: open there instead of centred.
        if (saved != null && FitToMonitors(saved) != null)
            window.WindowStartupLocation = WindowStartupLocation.Manual;

        window.SourceInitialized += (_, _) => Restore(window);
        window.Closing += (_, _) => Save(window);

        // Also save shortly after the user moves or resizes, so a crash or kill does not lose it.
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            Save(window);
        };
        window.LocationChanged += (_, _) => { debounce.Stop(); debounce.Start(); };
        window.SizeChanged += (_, _) => { debounce.Stop(); debounce.Start(); };
    }

    private static void Restore(Window window)
    {
        var saved = Load();
        if (saved == null)
            return;

        var fit = FitToMonitors(saved);
        if (fit is not { } rect)
            return; // monitor gone or nonsense data: keep the default centred position

        var handle = new WindowInteropHelper(window).Handle;
        var placement = new NativeMethods.WINDOWPLACEMENT { length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        if (!NativeMethods.GetWindowPlacement(handle, ref placement))
            return;

        placement.rcNormalPosition = new NativeMethods.RECT { Left = rect.Left, Top = rect.Top, Right = rect.Right, Bottom = rect.Bottom };
        placement.showCmd = saved.Maximized ? NativeMethods.SW_SHOWMAXIMIZED : NativeMethods.SW_SHOWNORMAL;
        NativeMethods.SetWindowPlacement(handle, ref placement);
    }

    private static void Save(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;

            var placement = new NativeMethods.WINDOWPLACEMENT { length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
            if (!NativeMethods.GetWindowPlacement(handle, ref placement))
                return;

            var n = placement.rcNormalPosition;
            var info = new SavedWindowPlacement
            {
                Left = n.Left, Top = n.Top, Right = n.Right, Bottom = n.Bottom,
                // a minimized window is remembered at its normal size
                Maximized = placement.showCmd == NativeMethods.SW_SHOWMAXIMIZED,
                Monitor = MonitorNameFor(handle)
            };

            File.WriteAllText(FilePath, WindowPlacementRules.Serialize(info), new UTF8Encoding(false));
        }
        catch
        {
            // read-only folder or window already gone: not worth interrupting the user
        }
    }

    private static SavedWindowPlacement? Load()
    {
        try
        {
            return File.Exists(FilePath) ? WindowPlacementRules.Deserialize(File.ReadAllText(FilePath)) : null;
        }
        catch
        {
            return null;
        }
    }

    private static PixelRect? FitToMonitors(SavedWindowPlacement saved) =>
        WindowPlacementRules.Fit(saved.Rect, WorkAreas());

    /// <summary>Usable area (excluding the taskbar) of every connected monitor, in virtual-screen pixels.</summary>
    private static List<PixelRect> WorkAreas()
    {
        var areas = new List<PixelRect>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
        {
            var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
            if (NativeMethods.GetMonitorInfo(monitor, ref info))
                areas.Add(new PixelRect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom));
            return true;
        }, IntPtr.Zero);

        return areas;
    }

    private static string MonitorNameFor(IntPtr handle)
    {
        var monitor = NativeMethods.MonitorFromWindow(handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        return NativeMethods.GetMonitorInfo(monitor, ref info) ? info.szDevice : "";
    }

    private static class NativeMethods
    {
        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOWMAXIMIZED = 3;
        public const int MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);

        [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);
        [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);
        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, int flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
    }
}
