<#
.SYNOPSIS
  Lines the Windows display arrangement up with where the side screens physically sit, by eye.
.DESCRIPTION
  Run it once per rig, with Nuclear Option closed, and again only if a screen is moved. It never
  runs alongside the game, and nothing depends on it.

  The game window spans a band as tall as the center screen, and the Windows arrangement decides
  which rows of each side screen the band lights. HUD pieces crossing a gap split by however far
  the arrangement is off. This tool covers the center and side screens in dark windows with tick
  marks along each edge where two screens meet, at the same desktop rows on both sides of the gap,
  so they line up across the bezel exactly when the arrangement matches the physical screens.

  Keys: Left/Right or Tab pick a side screen; Up/Down slide its ticks 1 px (Shift: 10) until they
  continue the center's; R resets; P previews the plugin's own suggestion, from VerticalOffsetMm in
  nuclear-triad.cfg; Enter moves the screens in Windows; Esc quits. Sliding only previews. Enter
  first writes every display's position to .tools\display-undo\<time>.json, then moves the side
  screens vertically (the primary stays at 0,0), reads back what Windows set and redraws on the
  new arrangement, for a check or another pass.

  powershell -NoProfile -ExecutionPolicy Bypass -File tools\align-screens.ps1
  ... -DryRun [-LeftShift n] [-RightShift n]   prints the screens, the tick rows and the calls Enter would make
  ... -Undo .tools\display-undo\<time>.json    puts every display back where an undo file says
  ... -Undo <file> -DryRun                     prints what that undo would change
  ... -Snapshot <folder>                       saves each screen's picture as a PNG instead of showing it

  -LeftShift and -RightShift are moves in Windows, n px down (negative: up); the preview starts
  there. -DryRun and -Snapshot change nothing and open no windows. The interactive mode and -Undo
  refuse to run while Nuclear Option is running.
#>
param(
    [switch]$DryRun,
    [string]$Undo,
    [string]$Snapshot,
    [int]$LeftShift = 0,
    [int]$RightShift = 0,
    [string]$GameDir = $(if ($env:NUCLEAR_OPTION_GAME_DIR) { $env:NUCLEAR_OPTION_GAME_DIR } else { 'C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option' })
)
$ErrorActionPreference = 'Stop'
if ($Snapshot -and ($DryRun -or $Undo)) { throw 'Use -Snapshot on its own.' }

$repo = Split-Path -Parent $PSScriptRoot
$undoDir = Join-Path $repo '.tools\display-undo'
$configPath = Join-Path $GameDir 'BepInEx\config\nuclear-triad.cfg'

Add-Type -Language CSharp -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AlignScreens
{
    public class Monitor
    {
        public string Device;
        public int X, Y, W, H;
        public bool Primary;

        // DISPLAY2 for \\.\DISPLAY2, the way nuclear-triad.cfg names monitors.
        public string Name { get { return Device.Substring(Device.LastIndexOf('\\') + 1); } }
    }

    public static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int left; public int top; public int right; public int bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct MONITORINFOEX { public uint cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct DEVMODE {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public ushort dmSpecVersion; public ushort dmDriverVersion; public ushort dmSize; public ushort dmDriverExtra; public uint dmFields;
            public int dmPositionX; public int dmPositionY; public uint dmDisplayOrientation; public uint dmDisplayFixedOutput;
            public short dmColor; public short dmDuplex; public short dmYResolution; public short dmTTOption; public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public ushort dmLogPixels; public uint dmBitsPerPel; public uint dmPelsWidth; public uint dmPelsHeight; public uint dmDisplayFlags; public uint dmDisplayFrequency;
            public uint dmICMMethod; public uint dmICMIntent; public uint dmMediaType; public uint dmDitherType; public uint dmReserved1; public uint dmReserved2; public uint dmPanningWidth; public uint dmPanningHeight;
        }
        delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr dwData);
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettingsEx(string device, int mode, ref DEVMODE dm, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string device, ref DEVMODE dm, IntPtr hwnd, uint flags, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string device, IntPtr dm, IntPtr hwnd, uint flags, IntPtr lParam);

        const int ENUM_CURRENT_SETTINGS = -1;
        const uint DM_POSITION = 0x20;
        const uint CDS_UPDATEREGISTRY = 0x1;
        const uint CDS_NORESET = 0x10000000;
        const int DISP_CHANGE_FAILED = -1;
        static readonly IntPtr PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        // Every coordinate this tool uses, windows included, is in physical pixels.
        public static void BeDpiAware()
        {
            if (!SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)) SetThreadDpiAwarenessContext(PER_MONITOR_AWARE_V2);
        }

        public static List<Monitor> Monitors()
        {
            BeDpiAware();
            var list = new List<Monitor>();
            MonitorEnumProc callback = delegate(IntPtr h, IntPtr dc, IntPtr r, IntPtr d)
            {
                var info = new MONITORINFOEX();
                info.cbSize = (uint)Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(h, ref info))
                    list.Add(new Monitor {
                        Device = info.szDevice,
                        X = info.rcMonitor.left, Y = info.rcMonitor.top,
                        W = info.rcMonitor.right - info.rcMonitor.left, H = info.rcMonitor.bottom - info.rcMonitor.top,
                        Primary = (info.dwFlags & 1) != 0
                    });
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return list;
        }

        // Writes a display's new position to the registry without applying it. Commit applies
        // every staged position at once. Both return a DISP_CHANGE code; 0 is success.
        public static int Stage(string device, int x, int y)
        {
            var dm = new DEVMODE();
            dm.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
            if (!EnumDisplaySettingsEx(device, ENUM_CURRENT_SETTINGS, ref dm, 0)) return DISP_CHANGE_FAILED;
            dm.dmFields = DM_POSITION;
            dm.dmPositionX = x;
            dm.dmPositionY = y;
            return ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_NORESET, IntPtr.Zero);
        }

        public static int Commit()
        {
            return ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        }
    }

    // A side screen in the preview.
    public class Side
    {
        public string Role;          // "left" or "right"
        public Monitor Monitor;
        public int Shift;            // the previewed move in Windows: px down, negative up
        public bool HasSuggestion;
        public int Suggestion;       // the plugin's level, as a move in the same sense
        public int Min = int.MinValue, Max = int.MaxValue;  // moves that keep it touching the center

        public int Clamp(int shift) { return Math.Max(Min, Math.Min(Max, shift)); }

        public static string MoveText(int shift)
        {
            if (shift == 0) return "no move";
            return string.Format("{0} px {1}", Math.Abs(shift), shift > 0 ? "down" : "up");
        }
    }

    public class Scene
    {
        public Monitor Center;
        public List<Side> Sides = new List<Side>();        // left first
        public int Selected;
        public string Status = "";                           // what the last Enter did, one line per \n
        public List<string> Warnings = new List<string>();
        public bool Apply;                                   // how the last Ui.Run ended

        public Side SideOn(Monitor m) { foreach (Side s in Sides) if (s.Monitor == m) return s; return null; }
        public bool Has(string role) { foreach (Side s in Sides) if (s.Role == role) return true; return false; }
        public void Select(string role) { for (int i = 0; i < Sides.Count; i++) if (Sides[i].Role == role) Selected = i; }
        public bool AnyMove() { foreach (Side s in Sides) if (s.Shift != 0) return true; return false; }
        public bool AnySuggestion() { foreach (Side s in Sides) if (s.HasSuggestion) return true; return false; }
    }

    // What each screen shows. Ticks sit at the center screen's desktop rows, every 10 px, and run
    // right to the edge pixel on both sides of each gap: short and grey every 10, long and white
    // every 50, longer, thicker, numbered and coloured every 100, in a cycle of five colours. Being
    // one tick, one hundred or five hundred rows off each looks plainly wrong.
    public static class Painter
    {
        public const int Spacing = 10;
        const int FineLength = 18, HalfLength = 44, HundredLength = 90;

        static readonly Color Outside = Color.FromArgb(8, 8, 8);
        static readonly Color Lit = Color.FromArgb(30, 30, 30);
        static readonly Color FineTick = Color.FromArgb(165, 165, 165);
        static readonly Color HalfTick = Color.White;
        static readonly Color[] HundredTicks = {
            Color.FromArgb(255, 90, 90), Color.FromArgb(255, 205, 40), Color.FromArgb(90, 230, 90),
            Color.FromArgb(80, 200, 255), Color.FromArgb(255, 115, 255) };
        static readonly Color Body = Color.FromArgb(225, 225, 225);
        static readonly Color Dim = Color.FromArgb(140, 140, 140);
        static readonly Color Chosen = Color.FromArgb(255, 210, 60);
        static readonly Color Good = Color.FromArgb(120, 230, 120);
        static readonly Color Warn = Color.FromArgb(255, 145, 60);

        static readonly Font LabelFont = new Font("Segoe UI", 22, FontStyle.Bold, GraphicsUnit.Pixel);
        static readonly Font TitleFont = new Font("Segoe UI", 52, FontStyle.Bold, GraphicsUnit.Pixel);
        static readonly Font TextFont = new Font("Segoe UI", 30, FontStyle.Regular, GraphicsUnit.Pixel);
        static readonly Font SmallFont = new Font("Segoe UI", 26, FontStyle.Regular, GraphicsUnit.Pixel);

        public static void Paint(Graphics g, Monitor m, Scene s)
        {
            g.Clear(Outside);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            Side side = s.SideOn(m);
            Monitor c = s.Center;

            // A desktop row's row on this screen. A side screen is drawn as it will be once Windows
            // moves it down by its preview move: the same desktop row lands that many rows higher.
            int origin = m.Y + (side == null ? 0 : side.Shift);

            // The band: the center screen's desktop rows, which the game window lights.
            using (var lit = new SolidBrush(Lit)) g.FillRectangle(lit, 0, c.Y - origin, m.W, c.H);

            for (int y = c.Y; y < c.Y + c.H; y += Spacing)
            {
                int row = y - origin, n = y - c.Y;
                if (side != null) Tick(g, m, row, n, side.Role == "right");
                else
                {
                    if (s.Has("left")) Tick(g, m, row, n, true);
                    if (s.Has("right")) Tick(g, m, row, n, false);
                }
            }
            if (side == null) CenterText(g, m, s);
            else SideText(g, m, s, side, c.Y - origin + c.H / 2);
        }

        // One tick at a row of this screen, from the edge pixel inward; atLeft puts it at x = 0.
        // n is the row's number on the center screen, which labels every hundredth.
        static void Tick(Graphics g, Monitor m, int row, int n, bool atLeft)
        {
            int length = FineLength, thick = 1;
            Color color = FineTick;
            if (n % 100 == 0) { length = HundredLength; thick = 3; color = HundredTicks[n / 100 % HundredTicks.Length]; }
            else if (n % 50 == 0) { length = HalfLength; color = HalfTick; }

            using (var brush = new SolidBrush(color))
            {
                g.FillRectangle(brush, atLeft ? 0 : m.W - length, row - thick / 2, length, thick);
                if (n % 100 != 0) return;
                string label = n.ToString();
                SizeF size = g.MeasureString(label, LabelFont);
                // Beside its tick, but kept on the screen; the tick, not the label, is what lines up.
                float x = atLeft ? length + 6 : m.W - length - 6 - size.Width;
                float y = Math.Max(0, Math.Min(m.H - size.Height, row - size.Height / 2));
                g.DrawString(label, LabelFont, brush, x, y);
            }
        }

        struct Line
        {
            public string Text; public Font Font; public Color Color;
            public Line(string text, Font font, Color color) { Text = text; Font = font; Color = color; }
        }

        // Lines stacked about (cx, cy), each centered.
        static void Block(Graphics g, List<Line> lines, float cx, float cy)
        {
            var sizes = new List<SizeF>();
            float total = 0;
            foreach (Line l in lines)
            {
                SizeF size = g.MeasureString(l.Text.Length == 0 ? " " : l.Text, l.Font);
                sizes.Add(size);
                total += size.Height;
            }
            float y = cy - total / 2;
            for (int i = 0; i < lines.Count; i++)
            {
                using (var brush = new SolidBrush(lines[i].Color))
                    g.DrawString(lines[i].Text, lines[i].Font, brush, cx - sizes[i].Width / 2, y);
                y += sizes[i].Height;
            }
        }

        static void CenterText(Graphics g, Monitor m, Scene s)
        {
            var lines = new List<Line>();
            lines.Add(new Line("Line up the screens", TitleFont, Body));
            lines.Add(new Line("Slide each side screen's ticks until they continue the center screen's across the gap:", TextFont, Body));
            lines.Add(new Line("the numbers first, then the colours, then the fine ticks.", TextFont, Body));
            lines.Add(new Line("", TextFont, Body));
            lines.Add(new Line("\u2190 \u2192 or Tab: pick a side      \u2191 \u2193: slide its ticks 1 px (Shift: 10)      R: reset" +
                (s.AnySuggestion() ? "      P: the plugin's suggestion" : ""), SmallFont, Dim));
            lines.Add(new Line("Enter: move the screens in Windows (an undo file is written first)      Esc: quit", SmallFont, Dim));
            lines.Add(new Line("", TextFont, Body));
            for (int i = 0; i < s.Sides.Count; i++)
            {
                Side side = s.Sides[i];
                string name = side.Role == "left" ? "Left" : "Right";
                string text = side.Shift == 0
                    ? string.Format("{0}: no move (top {1})", name, side.Monitor.Y)
                    : string.Format("{0}: Enter moves it {1} in Windows (top {2} to {3})", name, Side.MoveText(side.Shift), side.Monitor.Y, side.Monitor.Y + side.Shift);
                if (side.HasSuggestion) text += string.Format("      plugin: {0}", Side.MoveText(side.Suggestion));
                lines.Add(new Line((i == s.Selected ? "\u25B6 " : "") + text, TextFont, i == s.Selected ? Chosen : Body));
            }
            if (!string.IsNullOrEmpty(s.Status))
            {
                lines.Add(new Line("", TextFont, Body));
                foreach (string l in s.Status.Split('\n')) lines.Add(new Line(l, SmallFont, Good));
            }
            if (s.Warnings.Count > 0)
            {
                lines.Add(new Line("", TextFont, Body));
                foreach (string w in s.Warnings) lines.Add(new Line(w, SmallFont, Warn));
            }
            Block(g, lines, m.W / 2f, m.H / 2f);
        }

        static void SideText(Graphics g, Monitor m, Scene s, Side side, int bandMiddle)
        {
            bool chosen = s.Sides[s.Selected] == side;
            var lines = new List<Line>();
            lines.Add(new Line(side.Role == "left" ? "LEFT" : "RIGHT", TitleFont, chosen ? Chosen : Dim));
            lines.Add(new Line(chosen ? "selected: \u2191 \u2193 slide its ticks" : (side.Role == "left" ? "\u2190 selects it" : "\u2192 selects it"), SmallFont, chosen ? Chosen : Dim));
            lines.Add(new Line("", TextFont, Body));
            if (side.Shift == 0) lines.Add(new Line("No move", TextFont, Body));
            else
            {
                lines.Add(new Line("Enter moves this screen", TextFont, Body));
                lines.Add(new Line(Side.MoveText(side.Shift) + " in Windows", TextFont, Body));
                lines.Add(new Line(string.Format("top {0} to {1}", side.Monitor.Y, side.Monitor.Y + side.Shift), SmallFont, Dim));
            }
            Block(g, lines, m.W / 2f, Math.Max(200, Math.Min(m.H - 200, bandMiddle)));
        }
    }

    // One borderless, topmost window covering a whole screen, taskbar included.
    public class ScreenForm : Form
    {
        readonly Monitor monitor;
        readonly Ui ui;

        public ScreenForm(Monitor m, Ui owner, bool inTaskbar)
        {
            monitor = m;
            ui = owner;
            Text = "Line up the screens";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = inTaskbar;
            TopMost = true;
            BackColor = Color.Black;
            DoubleBuffered = true;
            Bounds = new Rectangle(m.X, m.Y, m.W, m.H);
        }

        protected override void OnPaint(PaintEventArgs e) { Painter.Paint(e.Graphics, monitor, ui.Scene); }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) { return ui.Key(keyData) || base.ProcessCmdKey(ref msg, keyData); }
        protected override void OnFormClosed(FormClosedEventArgs e) { base.OnFormClosed(e); ui.Quit(); }
    }

    public class Ui
    {
        public readonly Scene Scene;
        readonly List<ScreenForm> forms = new List<ScreenForm>();
        readonly ApplicationContext context = new ApplicationContext();
        bool done;

        Ui(Scene scene) { Scene = scene; }

        // Shows the screens until Enter (true: apply the previewed moves) or Esc (false).
        public static bool Run(Scene scene)
        {
            Native.BeDpiAware();
            var ui = new Ui(scene);
            ui.forms.Add(new ScreenForm(scene.Center, ui, true));
            foreach (Side side in scene.Sides) ui.forms.Add(new ScreenForm(side.Monitor, ui, false));
            foreach (ScreenForm f in ui.forms) f.Show();
            ui.forms[0].Activate();
            Application.Run(ui.context);
            ui.done = true;
            foreach (ScreenForm f in ui.forms) f.Dispose();
            return scene.Apply;
        }

        // Each screen's picture as a PNG named by role and display, without opening a window.
        public static List<string> Snapshot(Scene scene, string folder)
        {
            Native.BeDpiAware();
            var files = new List<string>();
            var screens = new List<Monitor> { scene.Center };
            foreach (Side side in scene.Sides) screens.Add(side.Monitor);
            foreach (Monitor m in screens)
            {
                Side side = scene.SideOn(m);
                string path = Path.Combine(folder, (side == null ? "center" : side.Role) + "-" + m.Name + ".png");
                using (var bitmap = new Bitmap(m.W, m.H, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(bitmap)) Painter.Paint(g, m, scene);
                    bitmap.Save(path, ImageFormat.Png);
                }
                files.Add(path);
            }
            return files;
        }

        public bool Key(Keys keyData)
        {
            if (done) return false;
            Keys key = keyData & Keys.KeyCode;
            int step = (keyData & Keys.Shift) == Keys.Shift ? 10 : 1;
            Side side = Scene.Sides[Scene.Selected];
            switch (key)
            {
                // Ticks up means the screen moves down in Windows, and the other way round.
                case Keys.Up: side.Shift = side.Clamp(side.Shift + step); break;
                case Keys.Down: side.Shift = side.Clamp(side.Shift - step); break;
                case Keys.Left: Scene.Select("left"); break;
                case Keys.Right: Scene.Select("right"); break;
                case Keys.Tab: Scene.Selected = (Scene.Selected + 1) % Scene.Sides.Count; break;
                case Keys.R: foreach (Side s in Scene.Sides) s.Shift = 0; break;
                case Keys.P: foreach (Side s in Scene.Sides) if (s.HasSuggestion) s.Shift = s.Clamp(s.Suggestion); break;
                case Keys.Enter:
                    if (Scene.AnyMove()) { Finish(true); return true; }
                    Scene.Status = "Nothing to apply: no screen is set to move.";
                    break;
                case Keys.Escape: Finish(false); return true;
                default: return false;
            }
            foreach (ScreenForm f in forms) f.Invalidate();
            return true;
        }

        public void Quit() { Finish(false); }

        void Finish(bool apply)
        {
            if (done) return;
            done = true;
            Scene.Apply = apply;
            context.ExitThread();
        }
    }
}
'@

function Format-Move([int]$px) {
    if ($px -eq 0) { 'no move' } elseif ($px -gt 0) { "$px px down" } else { "$(-$px) px up" }
}

function Get-RelativePath([string]$path) {
    if ($path.StartsWith("$repo\", [StringComparison]::OrdinalIgnoreCase)) { $path.Substring($repo.Length + 1) } else { $path }
}

function Get-DispChangeName([int]$code) {
    $names = 'DISP_CHANGE_BADDUALVIEW', 'DISP_CHANGE_BADPARAM', 'DISP_CHANGE_BADFLAGS', 'DISP_CHANGE_NOTUPDATED',
        'DISP_CHANGE_BADMODE', 'DISP_CHANGE_FAILED', 'DISP_CHANGE_SUCCESSFUL', 'DISP_CHANGE_RESTART'
    if ($code -ge -6 -and $code -le 1) { $names[$code + 6] } else { "code $code" }
}

# nuclear-triad.cfg as 'Section.Key' -> value, or $null without one.
function Read-PluginConfig([string]$path) {
    if (-not (Test-Path $path)) { return $null }
    $config = @{}
    $section = ''
    foreach ($line in Get-Content $path) {
        if ($line -match '^\s*\[(.+)\]\s*$') { $section = $Matches[1] }
        elseif ($line -match '^\s*([^#\s][^=]*?)\s*=\s*(.*?)\s*$') { $config["$section.$($Matches[1])"] = $Matches[2] }
    }
    $config
}

# The center is the primary display; left and right are the displays whose edges touch it.
function Get-Rig {
    $all = [AlignScreens.Native]::Monitors()
    $center = @($all | Where-Object { $_.Primary })[0]
    if (-not $center) { throw 'Windows reports no primary display.' }
    $beside = @($all | Where-Object { $_.Y -lt $center.Y + $center.H -and $_.Y + $_.H -gt $center.Y })
    $left = @($beside | Where-Object { $_.X + $_.W -eq $center.X })
    $right = @($beside | Where-Object { $_.X -eq $center.X + $center.W })
    if ($left.Count -gt 1 -or $right.Count -gt 1) { throw 'More than one display touches one side of the primary; this tool expects one screen each side.' }
    if ($left.Count + $right.Count -eq 0) { throw "No display touches the primary display's left or right edge." }
    [pscustomobject]@{ All = $all; Center = $center; Left = $left[0]; Right = $right[0] }
}

# Where nuclear-triad.cfg disagrees with the screens found.
function Get-ConfigWarnings($config, $rig) {
    if (-not $config) { return }
    foreach ($role in 'Center', 'Left', 'Right') {
        if ($config["Geometry.$role.PixelRect"]) {
            "nuclear-triad.cfg sets Geometry.$role.PixelRect, so the plugin uses that rect for the $($role.ToLower()) screen, not a monitor."
            continue
        }
        $want = $config["Geometry.$role.Monitor"]
        $have = if ($rig.$role) { $rig.$role.Name } else { 'none' }
        if ($want -and $want -ne $have) { "nuclear-triad.cfg says the $($role.ToLower()) screen is $want, but Windows has $have there." }
    }
}

# The plugin's level for a side screen, as a move in px down (negative: up), or $null. The same
# sums as its rig-status band report (Layout.BandOffsetMm): the screen's middle sits
# VerticalOffsetMm above the center's middle, at WidthMm over the screen's pixel width.
function Get-Suggestion($config, [string]$section, $side, $center) {
    if (-not $config -or -not $side -or $config['Geometry.VerticalFromDesktop'] -eq 'true') { return $null }
    $offset = $config["Geometry.$section.VerticalOffsetMm"]
    $width = $config["Geometry.$section.WidthMm"]
    if (-not $offset -or -not $width) { return $null }
    $culture = [Globalization.CultureInfo]::InvariantCulture
    $mmPerPx = [double]::Parse($width, $culture) / $side.W
    $levelTop = $center.Y + $center.H / 2.0 - [double]::Parse($offset, $culture) / $mmPerPx - $side.H / 2.0
    [int][math]::Round($levelTop) - $side.Y
}

function New-Scene($rig, $config, [int]$leftShift, [int]$rightShift, [string]$status) {
    $scene = New-Object AlignScreens.Scene
    $scene.Center = $rig.Center
    foreach ($entry in @(@('left', 'Left', $rig.Left, $leftShift), @('right', 'Right', $rig.Right, $rightShift))) {
        $role, $section, $monitor, $shift = $entry
        if (-not $monitor) { continue }
        $side = New-Object AlignScreens.Side
        $side.Role = $role
        $side.Monitor = $monitor
        # Moves that keep at least one row of the screen beside the center, so the two still touch.
        $side.Min = $rig.Center.Y + 1 - $monitor.Y - $monitor.H
        $side.Max = $rig.Center.Y + $rig.Center.H - 1 - $monitor.Y
        $side.Shift = $side.Clamp($shift)
        $suggestion = Get-Suggestion $config $section $monitor $rig.Center
        if ($null -ne $suggestion) { $side.HasSuggestion = $true; $side.Suggestion = $suggestion }
        $scene.Sides.Add($side)
    }
    $scene.Status = $status
    foreach ($warning in @(Get-ConfigWarnings $config $rig)) { $scene.Warnings.Add($warning) }
    $scene
}

# The previewed moves as new positions: side screens only, vertical only.
function Get-Moves($scene) {
    foreach ($side in $scene.Sides) {
        if ($side.Shift -eq 0) { continue }
        $m = $side.Monitor
        [pscustomobject]@{ Role = $side.Role; Device = $m.Device; X = $m.X; Y = $m.Y + $side.Shift; FromX = $m.X; FromY = $m.Y }
    }
}

# Every display's position, written before anything moves; -Undo puts them back.
function Save-Undo($monitors, [string]$reason) {
    New-Item -ItemType Directory -Force -Path $undoDir | Out-Null
    $path = Join-Path $undoDir ((Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.json')
    [pscustomobject]@{
        Written = (Get-Date).ToString('o')
        Reason = $reason
        Displays = @($monitors | ForEach-Object { [pscustomobject]@{ Device = $_.Device; X = $_.X; Y = $_.Y; W = $_.W; H = $_.H; Primary = $_.Primary } })
    } | ConvertTo-Json -Depth 4 | Set-Content -Path $path -Encoding UTF8
    $path
}

# Moves displays: each new position is staged in the registry, then one call applies them all.
# If Windows refuses any of it, the old positions are staged back and applied, so nothing
# half-done is left waiting for the next display change.
function Set-Positions($moves) {
    $staged = @()
    $failure = $null
    foreach ($move in $moves) {
        $code = [AlignScreens.Native]::Stage($move.Device, $move.X, $move.Y)
        if ($code -ne 0) { $failure = "Windows refused to stage $($move.Device) at ($($move.X), $($move.Y)): $(Get-DispChangeName $code)"; break }
        $staged += $move
    }
    if (-not $failure) {
        $code = [AlignScreens.Native]::Commit()
        if ($code -eq 0) { return }
        $failure = "Windows refused to apply the moves: $(Get-DispChangeName $code)"
    }
    foreach ($move in $staged) { [void][AlignScreens.Native]::Stage($move.Device, $move.FromX, $move.FromY) }
    [void][AlignScreens.Native]::Commit()
    throw "$failure. The old positions were staged back and applied; check with tools\display-probe.ps1."
}

# Reads the displays until every move shows or 3 seconds pass, and returns them.
function Wait-Positions($moves) {
    $deadline = (Get-Date).AddSeconds(3)
    while ($true) {
        $now = [AlignScreens.Native]::Monitors()
        $pending = @($moves | Where-Object { $move = $_; -not ($now | Where-Object { $_.Device -eq $move.Device -and $_.X -eq $move.X -and $_.Y -eq $move.Y }) })
        if ($pending.Count -eq 0 -or (Get-Date) -gt $deadline) { return $now }
        Start-Sleep -Milliseconds 250
    }
}

# One line per move: where it was, where Windows put it, and whether that's what was asked.
function Get-Report($moves, $now) {
    foreach ($move in $moves) {
        $label = "$($move.Role) ($($move.Device))"
        $d = @($now | Where-Object { $_.Device -eq $move.Device })[0]
        if (-not $d) { "${label}: no longer connected"; continue }
        $line = "${label}: ($($move.FromX), $($move.FromY)) -> ($($d.X), $($d.Y))"
        if ($d.X -eq $move.X -and $d.Y -eq $move.Y) { "$line, as asked" } else { "$line; asked for ($($move.X), $($move.Y))" }
    }
}

# The call that stages a move, as a dry run prints it.
function Format-Call($move) {
    $what = if ($move.X -eq $move.FromX) { Format-Move ($move.Y - $move.FromY) } else { "($($move.FromX), $($move.FromY)) -> ($($move.X), $($move.Y))" }
    "  ChangeDisplaySettingsEx(`"$($move.Device)`", {dmFields = DM_POSITION, dmPosition = ($($move.X), $($move.Y))}, NULL, CDS_UPDATEREGISTRY | CDS_NORESET, NULL)   $($move.Role): $what"
}

$gameRunning = [bool](Get-Process -Name NuclearOption -ErrorAction SilentlyContinue)

if ($Undo) {
    if (-not (Test-Path $Undo) -and (Test-Path (Join-Path $repo $Undo))) { $Undo = Join-Path $repo $Undo }
    $file = Get-Content -Raw -Path $Undo | ConvertFrom-Json
    if (-not $file.Displays) { throw "$Undo lists no displays; is it an undo file from this tool?" }
    $now = [AlignScreens.Native]::Monitors()
    $wasPrimary = @($file.Displays | Where-Object { $_.Primary })[0]
    $isPrimary = @($now | Where-Object { $_.Primary })[0]
    if ($wasPrimary -and $wasPrimary.Device -ne $isPrimary.Device) {
        throw "$($wasPrimary.Device) was the main display when the undo file was written. Make it the main display again in Windows' display settings, then run -Undo again."
    }
    $moves = @()
    foreach ($d in $file.Displays) {
        $current = @($now | Where-Object { $_.Device -eq $d.Device })[0]
        if (-not $current) { Write-Warning "$($d.Device) isn't connected now, so it's left out."; continue }
        if ($current.X -ne $d.X -or $current.Y -ne $d.Y) {
            $moves += [pscustomobject]@{ Role = $current.Name; Device = $current.Device; X = [int]$d.X; Y = [int]$d.Y; FromX = $current.X; FromY = $current.Y }
        }
    }
    if ($moves.Count -eq 0) { 'Every display is already where the undo file says. Nothing changed.'; return }
    if ($DryRun) {
        '-Undo would write a new undo file with every display''s position, then call:'
        $moves | ForEach-Object { Format-Call $_ }
        '  ChangeDisplaySettingsEx(NULL, NULL, NULL, 0, NULL)   applies them together'
        'Dry run: nothing changed.'
        return
    }
    if ($gameRunning) { throw 'Nuclear Option is running. Quit it first: -Undo moves screens under its window.' }
    $saved = Save-Undo $now "before align-screens -Undo $(Split-Path -Leaf $Undo)"
    "Undo file for this restore: $(Get-RelativePath $saved)"
    Set-Positions $moves
    Get-Report $moves (Wait-Positions $moves)
    return
}

$rig = Get-Rig
$config = Read-PluginConfig $configPath
if ($LeftShift -and -not $rig.Left) { Write-Warning 'No screen touches the primary on the left; -LeftShift is ignored.' }
if ($RightShift -and -not $rig.Right) { Write-Warning 'No screen touches the primary on the right; -RightShift is ignored.' }

if ($DryRun) {
    $scene = New-Scene $rig $config $LeftShift $RightShift ''
    $c = $rig.Center
    $roles = @{ $c.Device = 'center' }
    foreach ($side in $scene.Sides) { $roles[$side.Monitor.Device] = $side.Role }

    'Displays, in physical px (per-monitor DPI aware):'
    ($rig.All | Sort-Object X | Format-Table -AutoSize @(
        @{ n = 'Role'; e = { if ($roles.ContainsKey($_.Device)) { $roles[$_.Device] } else { '-' } } },
        'Device', 'Primary', 'X', 'Y', 'W', 'H') | Out-String -Width 200).TrimEnd()
    ''
    'The center is the primary display; left and right are the displays touching its left and right edges.'
    if (-not $config) { "No plugin config at ${configPath}: no cross-check and no plugin suggestion." }
    elseif ($scene.Warnings.Count -eq 0) {
        'nuclear-triad.cfg agrees: ' + ((@('Center', 'Left', 'Right') | Where-Object { $config["Geometry.$_.Monitor"] } |
            ForEach-Object { "Geometry.$_.Monitor = $($config["Geometry.$_.Monitor"])" }) -join ', ') + '.'
    }
    else { $scene.Warnings | ForEach-Object { "WARNING: $_" } }
    foreach ($side in $scene.Sides) {
        if ($side.HasSuggestion) { "The plugin's model has the $($side.Role) screen level at top $($side.Monitor.Y + $side.Suggestion): $(Format-Move $side.Suggestion), which P previews." }
        else { "No plugin suggestion for the $($side.Role) screen." }
        $asked = if ($side.Role -eq 'left') { $LeftShift } else { $RightShift }
        if ($asked -ne $side.Shift) { "The $($side.Role) move of $asked px would leave that screen not touching the center; held at $($side.Shift)." }
    }
    ''
    $last = $c.Y + [int][math]::Floor(($c.H - 1) / 10) * 10
    "Ticks every 10 px at the center's desktop rows $($c.Y) to $last (white every 50, numbered and coloured every 100), each running to the edge pixel:"
    $edges = @()
    if ($rig.Left) { $edges += 'x = 0' }
    if ($rig.Right) { $edges += "x = $($c.W - 1)" }
    "  center $($c.Name): rows 0 to $($last - $c.Y), at $($edges -join ' and ')"
    foreach ($side in $scene.Sides) {
        $m = $side.Monitor
        $origin = $m.Y + $side.Shift
        $x = if ($side.Role -eq 'left') { $m.W - 1 } else { 0 }
        $rows = @(for ($y = $c.Y; $y -le $last; $y += 10) { $y - $origin })
        $off = @($rows | Where-Object { $_ -lt 0 -or $_ -ge $m.H }).Count
        $line = "  $($side.Role) $($m.Name): rows $($rows[0]) to $($rows[-1]), at x = $x; the band lights rows $($c.Y - $origin) to $($c.Y + $c.H - 1 - $origin)"
        if ($off) { $line += "; $off ticks fall off the screen" }
        if ($side.Shift) { $line += " (previewing $(Format-Move $side.Shift): top $($m.Y) to $origin)" }
        $line
    }
    ''
    $moves = @(Get-Moves $scene)
    if ($moves.Count -eq 0) { 'No move previewed, so Enter would change nothing. Preview one with -LeftShift and -RightShift (px down; negative: up).' }
    else {
        "Enter would write .tools\display-undo\<time>.json with every display's position, then call:"
        $moves | ForEach-Object { Format-Call $_ }
        '  ChangeDisplaySettingsEx(NULL, NULL, NULL, 0, NULL)   applies them together'
        'then read the positions back and redraw on the new arrangement.'
    }
    if ($gameRunning) { 'Nuclear Option is running, so the interactive mode and -Undo would refuse until it is closed.' }
    'Dry run: nothing drawn, nothing changed.'
    return
}

if ($Snapshot) {
    New-Item -ItemType Directory -Force -Path $Snapshot | Out-Null
    $scene = New-Scene $rig $config $LeftShift $RightShift ''
    [AlignScreens.Ui]::Snapshot($scene, (Resolve-Path $Snapshot).Path)
    return
}

if ($gameRunning) { throw "Nuclear Option is running. Quit it first: this tool covers every screen, and Enter moves screens under the game's window." }
$status = ''
$firstUndo = $null
$selected = 0
while ($true) {
    $scene = New-Scene $rig $config $LeftShift $RightShift $status
    $scene.Selected = [math]::Min($selected, $scene.Sides.Count - 1)
    if (-not [AlignScreens.Ui]::Run($scene)) { break }
    $selected = $scene.Selected
    $moves = @(Get-Moves $scene)
    $saved = Save-Undo $rig.All ('before align-screens moved ' + (($moves | ForEach-Object { "$($_.Role) $(Format-Move ($_.Y - $_.FromY))" }) -join ', '))
    if (-not $firstUndo) { $firstUndo = $saved }
    "Undo file: $(Get-RelativePath $saved)"
    Set-Positions $moves
    $report = @(Get-Report $moves (Wait-Positions $moves))
    $report
    $status = (@('Applied:') + $report + @("Undo file: $(Get-RelativePath $saved)",
        'Check the ticks again: slide and press Enter for another pass, or Esc to finish.')) -join "`n"
    $rig = Get-Rig
    $LeftShift = 0
    $RightShift = 0
}
if (-not $firstUndo) { 'Quit; nothing applied.'; return }
'Done. To put the displays back as they were before this run:'
"  powershell -NoProfile -ExecutionPolicy Bypass -File tools\align-screens.ps1 -Undo $(Get-RelativePath $firstUndo)"
