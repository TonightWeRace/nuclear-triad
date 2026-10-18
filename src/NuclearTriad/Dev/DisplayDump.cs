using System;
using System.Linq;
using NuclearTriad.Geometry;
using NuclearTriad.Rig;
using UnityEngine;

namespace NuclearTriad.Dev
{
    /// <summary>
    /// dump-displays: the monitors and the game window as the game process sees them, and the
    /// camera rect each monitor would get, so coordinate or DPI trouble shows up before the rig
    /// depends on it.
    /// </summary>
    internal static class DisplayDump
    {
        const long WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_POPUP = 0x80000000;

        public static string Write(string outputDir, string label)
        {
            var report = new Report();
            report.Line($"Process DPI awareness: {Win32.DpiAwareness()}");
            report.Line($"Unity screen {Screen.width}x{Screen.height}, mode {Screen.fullScreenMode}, main window at {Screen.mainWindowPosition}");

            IntPtr window = Win32.GameWindow();
            if (window == IntPtr.Zero)
            {
                report.Line("Game window: not found");
                return $"game window not found -> {report.Save(outputDir, label, "displays")}";
            }

            long style = Win32.Style(window);
            PixelRect client = Win32.ClientRectOnScreen(window);
            report.Line($"Game window 0x{window.ToInt64():X}, style 0x{style:X8} (caption {(style & WS_CAPTION) != 0}, " +
                        $"resizable frame {(style & WS_THICKFRAME) != 0}, popup {(style & WS_POPUP) != 0})");
            report.Line($"  window rect {Win32.WindowRect(window)}, client rect on screen {client} ({client.Width}x{client.Height})");
            if (client.Width != Screen.width || client.Height != Screen.height)
                report.Line($"  NOTE: client size differs from Unity's screen size {Screen.width}x{Screen.height}");

            report.Line("");
            report.Line("Monitors, and the camera rect each would get in this window:");
            foreach (Win32.Monitor m in Win32.Monitors().OrderBy(m => m.Bounds.Left))
            {
                ViewportMapping v = Viewport.Map(m.Bounds, client);
                string mapping = v.IsEmpty
                    ? "outside the window"
                    : $"rect (x {v.X:F4}, y {v.Y:F4}, w {v.Width:F4}, h {v.Height:F4}), covers u {v.U0:F3}-{v.U1:F3}, v {v.V0:F3}-{v.V1:F3}";
                report.Line($"  {m.Device}{(m.Primary ? " (primary)" : "")} {m.Bounds} {m.Bounds.Width}x{m.Bounds.Height}: {mapping}");
            }

            return $"window {client.Width}x{client.Height} at ({client.Left}, {client.Top}) -> {report.Save(outputDir, label, "displays")}";
        }
    }
}
