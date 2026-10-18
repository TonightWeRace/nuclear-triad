using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using NuclearTriad.Geometry;

namespace NuclearTriad.Rig
{
    /// <summary>
    /// Plain black, borderless, always-on-top windows over the parts of the screens the game window
    /// doesn't cover, such as the strips above and below a center band, which would otherwise show
    /// the desktop and the side taskbars. They live on their own thread with their own message loop,
    /// never take focus, and have no taskbar buttons.
    /// </summary>
    internal sealed class Curtains : IDisposable
    {
        const string ClassName = "NuclearTriadCurtain";
        const uint WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000;
        const uint WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        const uint WM_QUIT = 0x0012;
        const int BLACK_BRUSH = 4;
        const int IDC_ARROW = 32512;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASSEX
        {
            public int cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int x;
            public int y;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "LoadCursorW")] static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
        [DllImport("gdi32.dll")] static extern IntPtr GetStockObject(int index);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

        readonly Thread thread;
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        readonly PixelRect[] rects;
        uint threadId;
        int created;

        public Curtains(IEnumerable<PixelRect> rects)
        {
            this.rects = rects.ToArray();
            thread = new Thread(Run) { IsBackground = true, Name = "Nuclear Triad curtains" };
            thread.Start();
            ready.WaitOne(2000);
        }

        public IReadOnlyList<PixelRect> Rects => rects;

        public int Created => created;

        /// <summary>The parts of each monitor outside the window.</summary>
        public static List<PixelRect> Uncovered(IEnumerable<PixelRect> monitors, PixelRect window) =>
            monitors.SelectMany(m => m.Minus(window)).ToList();

        void Run()
        {
            threadId = GetCurrentThreadId();
            IntPtr instance = GetModuleHandle(null);
            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf(typeof(WNDCLASSEX)),
                // Default handling is all a black window needs: the class brush paints it.
                lpfnWndProc = GetProcAddress(GetModuleHandle("user32.dll"), "DefWindowProcW"),
                hInstance = instance,
                hCursor = LoadCursor(IntPtr.Zero, new IntPtr(IDC_ARROW)),
                hbrBackground = GetStockObject(BLACK_BRUSH),
                lpszClassName = ClassName,
            };
            RegisterClassEx(ref wc); // Fails harmlessly when a previous set already registered it.

            var windows = new List<IntPtr>();
            foreach (PixelRect r in rects)
            {
                IntPtr hwnd = CreateWindowEx(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, ClassName, "Nuclear Triad curtain",
                    WS_POPUP | WS_VISIBLE, r.Left, r.Top, r.Width, r.Height, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
                if (hwnd != IntPtr.Zero) windows.Add(hwnd);
            }
            created = windows.Count;
            ready.Set();

            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            foreach (IntPtr hwnd in windows) DestroyWindow(hwnd);
        }

        public void Dispose()
        {
            if (threadId != 0) PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            thread.Join(2000);
        }
    }
}
