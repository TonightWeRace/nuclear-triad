using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NuclearTriad.Geometry;

namespace NuclearTriad.Rig
{
    /// <summary>
    /// The Win32 calls the rig needs: the monitors and the game window's client area, both in
    /// desktop pixels (origin top left of the primary monitor, y down).
    /// </summary>
    internal static class Win32
    {
        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);
        delegate bool WindowEnumProc(IntPtr hwnd, IntPtr data);

        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
        [DllImport("user32.dll")] static extern bool EnumWindows(WindowEnumProc proc, IntPtr data);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int maxCount);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")] static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateDC(string driver, string device, string output, IntPtr initData);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr hdc, int index);

        const int HORZSIZE = 4, VERTSIZE = 6;

        const int GWL_STYLE = -16;
        const int GWL_EXSTYLE = -20;
        const uint MONITORINFOF_PRIMARY = 1;
        public const long WS_CAPTION = 0x00C00000;
        public const long WS_THICKFRAME = 0x00040000;
        const long WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000, WS_CLIPSIBLINGS = 0x04000000, WS_CLIPCHILDREN = 0x02000000;
        const long WS_EX_DLGMODALFRAME = 0x1, WS_EX_WINDOWEDGE = 0x100, WS_EX_CLIENTEDGE = 0x200, WS_EX_STATICEDGE = 0x20000;
        const uint SWP_FRAMECHANGED = 0x0020, SWP_SHOWWINDOW = 0x0040, SWP_NOOWNERZORDER = 0x0200;

        public static bool IsAlive(IntPtr hwnd) => hwnd != IntPtr.Zero && IsWindow(hwnd);

        /// <summary>Makes the window a borderless popup whose client area is exactly the given desktop rect.</summary>
        public static void PlaceBorderless(IntPtr hwnd, PixelRect rect)
        {
            SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(WS_POPUP | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN));
            long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & ~(WS_EX_DLGMODALFRAME | WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
            SetWindowPos(hwnd, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height, SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOOWNERZORDER);
        }

        public sealed class Monitor
        {
            public string Device;
            public PixelRect Bounds;
            public bool Primary;
            /// <summary>The picture size the monitor reports to Windows (mm), turned as mounted; 0 when it reports none.</summary>
            public double WidthMm;
            public double HeightMm;
        }

        public static List<Monitor> Monitors()
        {
            var monitors = new List<Monitor>();
            MonitorEnumProc callback = (handle, hdc, rect, data) =>
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
                if (!GetMonitorInfo(handle, ref info)) return true;
                var monitor = new Monitor { Device = info.szDevice, Bounds = ToPixelRect(info.rcMonitor), Primary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0 };
                (monitor.WidthMm, monitor.HeightMm) = ReportedSize(info.szDevice, monitor.Bounds);
                monitors.Add(monitor);
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return monitors;
        }

        // The size Windows holds for the display, from the monitor's EDID: on the dev machine, 697 ×
        // 392 mm for the landscape center and 392 × 697 for each portrait side, as mounted. A device
        // context on the display answers for it alone. Zero when Windows has no size for it.
        static (double, double) ReportedSize(string device, PixelRect bounds)
        {
            IntPtr hdc = CreateDC(null, device, null, IntPtr.Zero);
            if (hdc == IntPtr.Zero) hdc = CreateDC(device, null, null, IntPtr.Zero);
            if (hdc == IntPtr.Zero) return (0, 0);
            try
            {
                return ScreenSize.Oriented(GetDeviceCaps(hdc, HORZSIZE), GetDeviceCaps(hdc, VERTSIZE), bounds.Width, bounds.Height);
            }
            finally
            {
                DeleteDC(hdc);
            }
        }

        /// <summary>The visible Unity window that belongs to this process, or zero.</summary>
        public static IntPtr GameWindow()
        {
            uint self = (uint)Process.GetCurrentProcess().Id;
            IntPtr found = IntPtr.Zero;
            var name = new StringBuilder(64);
            WindowEnumProc callback = (hwnd, data) =>
            {
                GetWindowThreadProcessId(hwnd, out uint owner);
                if (owner != self || !IsWindowVisible(hwnd)) return true;
                name.Length = 0;
                GetClassName(hwnd, name, name.Capacity);
                if (name.ToString() != "UnityWndClass") return true;
                found = hwnd;
                return false;
            };
            EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return found;
        }

        public static PixelRect ClientRectOnScreen(IntPtr hwnd)
        {
            GetClientRect(hwnd, out RECT client);
            var origin = new POINT();
            ClientToScreen(hwnd, ref origin);
            return new PixelRect(origin.X, origin.Y, origin.X + client.Right, origin.Y + client.Bottom);
        }

        public static PixelRect WindowRect(IntPtr hwnd)
        {
            GetWindowRect(hwnd, out RECT rect);
            return ToPixelRect(rect);
        }

        public static long Style(IntPtr hwnd) => GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();

        public static string DpiAwareness()
        {
            switch (GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()))
            {
                case 0: return "unaware (coordinates are virtualized under scaling)";
                case 1: return "system aware";
                case 2: return "per-monitor aware";
                default: return "unknown";
            }
        }

        static PixelRect ToPixelRect(RECT r) => new PixelRect(r.Left, r.Top, r.Right, r.Bottom);
    }
}
