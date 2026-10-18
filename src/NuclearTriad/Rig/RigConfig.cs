using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using NuclearTriad.Geometry;
using NuclearTriad.Tuning;
using UnityEngine;

namespace NuclearTriad.Rig
{
    internal enum SpanMode
    {
        /// <summary>As tall as the center screen; side screens render only the rows level with it.</summary>
        CenterBand,
        /// <summary>The bounding box of all three screens.</summary>
        FullHeight,
        /// <summary>Window.ExplicitRect, in desktop pixels.</summary>
        Explicit,
        /// <summary>Leave the game's window alone.</summary>
        Off,
    }

    internal enum HudScale
    {
        /// <summary>As big, from the real eye, as on the center screen.</summary>
        Physical,
        /// <summary>The wing's own rendered scale there: the ladder overlays the world exactly but grows outward.</summary>
        Rendered,
        /// <summary>The game's own size.</summary>
        None,
    }

    internal enum CenterFraming
    {
        /// <summary>Frame the center screen exactly as the game's FOV does, with the eyes centered.</summary>
        GameFov,
        /// <summary>Put the eye at the measured distance: physically exact, center FOV fixed by geometry.</summary>
        EyeDistance,
    }

    /// <summary>
    /// Everything the rig reads from BepInEx config, all editable live in ConfigurationManager (F1).
    /// F1 lists sections in the order they're first bound and settings in the order they're bound
    /// (Order counts down), so the binding order below is the layout someone setting up a rig
    /// reads: the "Start here" box, which opens the tuner; each screen's monitor and width, and
    /// each side screen's gap, angle and vertical offset, iRacing style; then the eye height, the
    /// sky grid, the window span and the hotkeys. Everything else is advanced, hidden until F1's
    /// "Advanced settings" box is ticked: the physical eye position and framing, the HUD and
    /// vegetation fixes, the BepInEx setup fix, and dev settings. The cfg file lists sections alphabetically instead, so no description says
    /// "above" or "below". Until Geometry.Measured is set, the desktop-plane test layout is used.
    /// Each screen's width starts at the size its monitor reports to Windows (Geometry/ScreenSize.cs),
    /// so a new install is close before anyone measures; the tuner's first step checks it.
    /// </summary>
    internal sealed class RigConfig
    {
        internal sealed class Screen
        {
            public string Role;
            public ConfigEntry<string> Monitor;
            public ConfigEntry<string> PixelRect;
            public ConfigEntry<float> WidthMm;
            public ConfigEntry<float> AngleDeg;
            public ConfigEntry<string> PhysicalAngleDeg;
            public ConfigEntry<float> GapMm;
            public ConfigEntry<float> VerticalOffsetMm;
        }

        public readonly ConfigEntry<bool> RigEnabled;
        public readonly ConfigEntry<bool> CockpitOnly;
        public readonly ConfigEntry<bool> HoldEyeInFreeLook;
        public readonly ConfigEntry<SpanMode> Span;
        public readonly ConfigEntry<string> ExplicitRect;
        public readonly ConfigEntry<bool> Curtains;
        public readonly ConfigEntry<bool> MenusOnCenter;
        public readonly ConfigEntry<string> OriginalScreen;
        public readonly ConfigEntry<bool> HudSideMarkers;
        public readonly ConfigEntry<bool> HudBendFlight;
        public readonly ConfigEntry<HudScale> HudBendScale;
        public readonly ConfigEntry<bool> HudFollowStraightAhead;
        public readonly ConfigEntry<CornerPlacement> HudCornerPanels;
        public readonly ConfigEntry<bool> VegetationAllScreens;
        public readonly ConfigEntry<float> VegetationMarginDeg;
        public readonly ConfigEntry<bool> FixBepInExManager;
        public readonly ConfigEntry<bool> Tuner;
        public readonly ConfigEntry<bool> SkyGrid;
        public readonly ConfigEntry<bool> Measured;
        public readonly ConfigEntry<CenterFraming> Framing;
        public readonly ConfigEntry<float> EyeDistanceMm;
        public readonly ConfigEntry<float> EyeHeightMm;
        public readonly ConfigEntry<float> EyeSideMm;
        public readonly ConfigEntry<bool> VerticalFromDesktop;
        public readonly Screen Left;
        public readonly Screen Center;
        public readonly Screen Right;
        public readonly ConfigEntry<KeyboardShortcut> ToggleKey;
        public readonly ConfigEntry<KeyboardShortcut> CaptureKey;
        public readonly ConfigEntry<KeyboardShortcut> GridKey;
        public readonly ConfigEntry<KeyboardShortcut> TunerKey;

        public RigConfig(ConfigFile config)
        {
            // First, so F1 opens on it: the tuner's steps get a rig close before anything is set by
            // hand. F1 shows it as a box with a button (Tuning/StartHere.cs), under its own heading;
            // the key stays in Geometry.
            Tuner = config.Bind("Geometry", "Tuner", false, new ConfigDescription(
                "Open the on-screen tuner. F1 shows it as the Start here box. Off at every game start.", null,
                new ConfigurationManagerAttributes
                {
                    Category = "Start here", Order = order--, HideSettingName = true, HideDefaultButton = true,
                    CustomDrawer = entry => StartHere.Draw(entry, TunerKey.Value),
                }));

            // Each width's default is what that screen's monitor reports, so the monitors are read
            // once here. Up to 0.1.28 the defaults were the dev machine's panels, 392, 697 and 392 mm,
            // which a rig of three ultrawides got as values.
            IList<Win32.Monitor> monitors = MonitorsForDefaults();
            Left = BindScreen(config, "left", side: true, monitors);
            Center = BindScreen(config, "center", side: false, monitors);
            Right = BindScreen(config, "right", side: true, monitors);

            Measured = config.Bind("Geometry", "Measured", false,
                Simple("Use the screens' widths, gaps, angles and offsets. Off: a flat test layout from the Windows arrangement. " +
                       "The tuner turns it on."));
            EyeHeightMm = config.Bind("Geometry", "EyeHeightMm", 0f,
                Simple("Your eyes above the middle of the center screen (mm), negative below. The tuner sets it; 0 is vanilla.",
                       OffsetRange));
            SkyGrid = config.Bind("Geometry", "SkyGrid", false,
                Simple("Show the sky grid: lines fixed in the sky that must run straight across every gap. Off at every game start."));
            EyeDistanceMm = config.Bind("Geometry", "EyeDistanceMm", 700f,
                Advanced("Your eyes' distance from the center screen (mm). Sizes the flight HUD on the side screens, and places the " +
                         "eye in EyeDistance framing.", SizeRange));
            Framing = config.Bind("Geometry", "CenterFraming", CenterFraming.GameFov,
                Advanced("GameFov: the center screen keeps the game's FOV, and zoom works. EyeDistance: the eye at EyeDistanceMm, " +
                         "for exact seams, with zoom off."));
            EyeSideMm = config.Bind("Geometry", "EyeSideMm", 0f,
                Advanced("Your eyes right of the center screen's middle (mm), negative left.", OffsetRange));
            VerticalFromDesktop = config.Bind("Geometry", "VerticalFromDesktop", false,
                Advanced("Take the side screens' heights from the Windows arrangement instead of VerticalOffsetMm. Only right if " +
                         "it matches the screens exactly."));

            Span = config.Bind("Window", "Span", SpanMode.CenterBand,
                Simple("How the game window covers the screens. CenterBand: as tall as the center screen, so the HUD and menus keep " +
                       "the game's size. FullHeight: every screen, top to bottom. Explicit: ExplicitRect. Off: the game's own window."));
            Curtains = config.Bind("Window", "Curtains", true,
                Advanced("Black out the parts of the screens outside the window, such as above and below a center band."));
            MenusOnCenter = config.Bind("Window", "MenusOnCenter", true,
                Advanced("Keep the game's menus on the center screen while the window spans several. Off: they spread across the " +
                         "whole window."));
            ExplicitRect = config.Bind("Window", "ExplicitRect", "",
                Advanced("The window's desktop rect 'left,top,right,bottom' in pixels, for Span = Explicit."));
            OriginalScreen = config.Bind("Window", "OriginalScreen", "", new ConfigDescription(
                "The game's own screen setting from before the span ('width,height,mode,x,y'), kept to put back even after a crash. " +
                "Filled in automatically.", null,
                new ConfigurationManagerAttributes { IsAdvanced = true, ReadOnly = true, Order = order-- }));

            TunerKey = config.Bind("Hotkeys", "Tuner", new KeyboardShortcut(KeyCode.U, KeyCode.RightControl), Simple("Open or close the tuner."));
            GridKey = config.Bind("Hotkeys", "SkyGrid", new KeyboardShortcut(KeyCode.G, KeyCode.RightControl), Simple("Show or hide the sky grid."));
            ToggleKey = config.Bind("Hotkeys", "ToggleRig", new KeyboardShortcut(KeyCode.T, KeyCode.RightControl),
                Simple("Switch the rig off and on until the game closes."));
            CaptureKey = config.Bind("Hotkeys", "Capture", new KeyboardShortcut(KeyCode.C, KeyCode.RightControl),
                Advanced("Save a capture with seam crops to Dev.OutputDir."));

            RigEnabled = config.Bind("Rig", "Enabled", true,
                Advanced("Build the three views in missions. Off: the game's camera fills the window. The ToggleRig hotkey switches " +
                         "the rig until the game closes, without changing this."));
            HoldEyeInFreeLook = config.Bind("Rig", "HoldEyeInFreeLook", true,
                Advanced("In free look, hold the eye still while the nose is on a screen, so the cockpit stays put under the flight " +
                         "HUD. Off: the game's lean, from 33° of pan. Head trackers are left alone."));
            CockpitOnly = config.Bind("Rig", "CockpitOnly", false, Advanced("Build the rig only in the cockpit view."));

            HudSideMarkers = config.Bind("Hud", "SideScreenMarkers", true,
                Advanced("Place the game's HUD markers on the side screens through those screens' own views, so they sit on their " +
                         "targets. Off: the game's placement, which drifts off them."));
            HudBendFlight = config.Bind("Hud", "BendFlightHud", true,
                Advanced("Place the flight HUD after the camera moves, so it doesn't lag the view, and turn it to a side screen's " +
                         "horizon when the nose is on one, sized per BendScale."));
            HudBendScale = config.Bind("Hud", "BendScale", HudScale.Physical,
                Advanced("How big the flight HUD is on a side screen. Physical: as big, from your eye, as on the center screen. " +
                         "Rendered: that screen's own scale. None: the game's size."));
            HudFollowStraightAhead = config.Bind("Hud", "FollowStraightAhead", true,
                Advanced("Keep the target designator and the off-screen arrows on straight ahead, which Geometry.EyeHeightMm moves. " +
                         "Off: they keep to the window's middle."));
            HudCornerPanels = config.Bind("Hud", "CornerPanels", CornerPlacement.CenterScreen,
                Advanced("Where the map, the weapons panel and the damage display go while the window spans several screens. " +
                         "CenterScreen: the center screen's corners. OuterCorners: the side screens' outer corners, as the game " +
                         "lays them out."));

            VegetationAllScreens = config.Bind("Vegetation", "CullAllScreens", true,
                Advanced("Cull trees and grass against all three screens. Off: against the center screen only, so they pop in and " +
                         "out on the side screens."));
            VegetationMarginDeg = config.Bind("Vegetation", "CullMarginDeg", 10f,
                Advanced("How far past the outer screen edges trees and grass are kept (degrees). More costs a little more drawing.",
                         new AcceptableValueRange<float>(0f, 45f)));

            FixBepInExManager = config.Bind("Setup", "FixBepInExManager", true,
                Advanced("Nuclear Option removes BepInEx's manager object as it starts, and every plugin with it, unless BepInEx's " +
                         "[Chainloader] HideManagerGameObject is on. On: if it's off, Nuclear Triad hides the object for this start and " +
                         "turns the setting on in BepInEx.cfg for the next. Off: BepInEx is left as it is."));

            DropRetired(config);
        }

        static IList<Win32.Monitor> MonitorsForDefaults()
        {
            try
            {
                return Win32.Monitors();
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning($"Couldn't read the monitors for the screens' default widths, so they start at a guess: {e.Message}");
                return new Win32.Monitor[0];
            }
        }

        // Monitor starts blank, so a new install takes the screens from Windows' arrangement however
        // its displays are numbered. Up to 0.1.27 it started as DISPLAY2, DISPLAY1 and DISPLAY3, the
        // dev machine's numbering, which BepInEx wrote into every cfg as a value. WidthMm starts at
        // the width that display reports to Windows, which needs Monitor and PixelRect bound first;
        // PixelRect is advanced, so it moves nothing in F1's simple view.
        Screen BindScreen(ConfigFile config, string role, bool side, IList<Win32.Monitor> monitors)
        {
            string section = "Geometry." + char.ToUpperInvariant(role[0]) + role.Substring(1);
            var screen = new Screen
            {
                Role = role,
                Monitor = config.Bind(section, "Monitor", "",
                    Simple("This screen's Windows display, such as DISPLAY2. Blank: taken from Windows' display arrangement, left to " +
                           "right.")),
            };
            screen.PixelRect = config.Bind(section, "PixelRect", "",
                Advanced("A desktop rect 'left,top,right,bottom' in pixels to use instead of Monitor, for NVIDIA Surround and the like."));
            Pick pick = Resolve(screen, monitors);
            float widthMm = (float)ScreenSize.DefaultWidthMm(pick.ReportedWidthMm, pick.Rect?.Width ?? 0);
            screen.WidthMm = config.Bind(section, "WidthMm", widthMm,
                Simple("Width of the picture, edge pixel to edge pixel (mm). Starts at what the monitor reports to Windows; the tuner's " +
                       "first step checks it against a tape measure.", SizeRange));
            if (side)
            {
                screen.GapMm = config.Bind(section, "GapMm", 0f,
                    Simple("Gap to the center screen's picture, edge pixel to edge pixel, straight across (mm).", GapRange));
                screen.AngleDeg = config.Bind(section, "AngleDeg", 0f,
                    Simple("How far this screen turns toward you from the center screen's plane (degrees); 0 is flat. Set it with the " +
                           "tuner: in the default framing it settles well below the real angle.", AngleRange));
                screen.VerticalOffsetMm = config.Bind(section, "VerticalOffsetMm", 0f,
                    Simple("This screen's middle above the center screen's middle (mm), negative below; 0 centers it. The tuner sets " +
                           "it.", OffsetRange));
                screen.PhysicalAngleDeg = config.Bind(section, "PhysicalAngleDeg", "",
                    Advanced("This screen's real angle (degrees), if AngleDeg holds a tuned one. It only sizes the flight HUD on this " +
                             "screen. Blank: AngleDeg is the real angle."));
            }
            return screen;
        }

        // Keys earlier builds wrote that nothing reads any more. BepInEx keeps a key it can't bind
        // in a private list and writes it back on every save, so they'd stay in the cfg for good.
        static readonly ConfigDefinition[] Retired =
        {
            new ConfigDefinition("Geometry", "TestLayoutFollowsGameFov"),
            new ConfigDefinition("Geometry.Left", "BottomOffsetMm"),
            new ConfigDefinition("Geometry.Right", "BottomOffsetMm"),
        };

        static void DropRetired(ConfigFile config)
        {
            PropertyInfo property = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.NonPublic);
            if (!(property?.GetValue(config, null) is IDictionary<ConfigDefinition, string> orphans)) return;
            var dropped = new List<string>();
            foreach (ConfigDefinition key in Retired)
                if (orphans.Remove(key)) dropped.Add($"{key.Section}.{key.Key}");
            if (dropped.Count == 0) return;
            config.Save();
            Plugin.Log?.LogInfo($"Dropped settings no build reads any more: {string.Join(", ", dropped.ToArray())}");
        }

        // Wide enough for any real rig. ConfigurationManager applies and clamps every keystroke, so
        // each range starts low enough that a value can still be typed digit by digit. Settings in
        // range can still describe a rig the eye can't see, such as an eye offset past a seam;
        // the rig then keeps its last layout that worked.
        static readonly AcceptableValueRange<float> SizeRange = new AcceptableValueRange<float>(1f, 5000f);
        static readonly AcceptableValueRange<float> GapRange = new AcceptableValueRange<float>(0f, 1000f);
        static readonly AcceptableValueRange<float> AngleRange = new AcceptableValueRange<float>(-20f, 90f);
        static readonly AcceptableValueRange<float> OffsetRange = new AcceptableValueRange<float>(-500f, 500f);

        // Each setting's F1 Order, counting down as they're bound.
        int order = 1000;

        ConfigDescription Simple(string text, AcceptableValueBase range = null) =>
            new ConfigDescription(text, range, new ConfigurationManagerAttributes { Order = order-- });

        ConfigDescription Advanced(string text, AcceptableValueBase range = null) =>
            new ConfigDescription(text, range, new ConfigurationManagerAttributes { IsAdvanced = true, Order = order-- });

        public IEnumerable<Screen> Screens => new[] { Left, Center, Right };

        /// <summary>Where a screen is on the desktop, and what put it there.</summary>
        public struct Pick
        {
            /// <summary>The screen's desktop rect, or null when nothing matches.</summary>
            public PixelRect? Rect;
            /// <summary>The display as Monitor names it (DISPLAY2); null for a PixelRect or none found.</summary>
            public string Display;
            public bool Primary;
            /// <summary>The screen's PixelRect is set, and wins over Monitor.</summary>
            public bool FromPixelRect;
            /// <summary>Monitor is blank, so the display comes from Windows' arrangement.</summary>
            public bool FromArrangement;
            /// <summary>The picture width the display reports to Windows (mm), as mounted; 0 when unknown or for a PixelRect.</summary>
            public double ReportedWidthMm;
        }

        /// <summary>
        /// A screen's place on the desktop: its PixelRect if set; else the display its Monitor names;
        /// else, with Monitor blank, its role in Windows' arrangement (Geometry/DisplayRow.cs).
        /// </summary>
        public Pick Resolve(Screen screen, IList<Win32.Monitor> monitors)
        {
            PixelRect? explicitRect = RectSetting(screen.PixelRect, "Monitor places the screen");
            if (explicitRect != null) return new Pick { Rect = explicitRect, FromPixelRect = true };
            string wanted = DeviceKey(screen.Monitor.Value);
            Win32.Monitor monitor;
            if (wanted.Length > 0) monitor = monitors.FirstOrDefault(m => DeviceKey(m.Device) == wanted);
            else
            {
                var row = DisplayRow.Find(monitors.Select(m => m.Bounds).ToList());
                monitor = row == null ? null
                    : monitors[screen.Role == "left" ? row.Value.Left : screen.Role == "right" ? row.Value.Right : row.Value.Center];
            }
            return new Pick
            {
                Rect = monitor?.Bounds, Display = monitor == null ? null : DeviceKey(monitor.Device), Primary = monitor?.Primary ?? false,
                FromArrangement = wanted.Length == 0, ReportedWidthMm = monitor?.WidthMm ?? 0,
            };
        }

        /// <summary>The desktop rect of a screen, or null when nothing matches (Resolve).</summary>
        public PixelRect? DesktopRect(Screen screen, IList<Win32.Monitor> monitors) => Resolve(screen, monitors).Rect;

        // Windows names monitors \\.\DISPLAY2, but BepInEx's config file doesn't round-trip
        // backslashes, so match on the part after the last one. A bare number means DISPLAY<n>.
        public static string DeviceKey(string name)
        {
            string trimmed = (name ?? "").Trim();
            string key = trimmed.Substring(trimmed.LastIndexOf('\\') + 1).ToUpperInvariant();
            return key.Length > 0 && key.All(char.IsDigit) ? "DISPLAY" + key : key;
        }

        /// <summary>The game window's target rect for the current span mode, or null to leave the window alone.</summary>
        public PixelRect? WindowRect(PixelRect left, PixelRect center, PixelRect right)
        {
            switch (Span.Value)
            {
                case SpanMode.CenterBand:
                    return new PixelRect(Math.Min(left.Left, center.Left), center.Top, Math.Max(right.Right, center.Right), center.Bottom);
                case SpanMode.FullHeight:
                    return PixelRect.Union(PixelRect.Union(left, center), right);
                case SpanMode.Explicit:
                    return RectSetting(ExplicitRect, "the window is left alone");
                default:
                    return null;
            }
        }

        // The bad value each rect setting was last warned about, so a typo is logged once rather
        // than at every refresh.
        readonly Dictionary<ConfigEntry<string>, string> badRects = new Dictionary<ConfigEntry<string>, string>();

        /// <summary>
        /// A 'left,top,right,bottom' setting's rect, or null while it's blank. A value that isn't four
        /// whole numbers counts as blank, with one warning in the log, so a typo in the cfg can't stop
        /// the plugin.
        /// </summary>
        PixelRect? RectSetting(ConfigEntry<string> setting, string whenBlank)
        {
            string text = (setting.Value ?? "").Trim();
            bool parsed = PixelRect.TryParse(text, out PixelRect rect);
            if (parsed || text.Length == 0)
            {
                badRects.Remove(setting);
                return parsed ? rect : (PixelRect?)null;
            }
            if (!badRects.TryGetValue(setting, out string warned) || warned != text)
            {
                badRects[setting] = text;
                Plugin.Log?.LogWarning($"{setting.Definition.Section}.{setting.Definition.Key} is '{text}', not 'left,top,right,bottom' " +
                                       $"in whole pixels, so it counts as blank and {whenBlank}.");
            }
            return null;
        }

        /// <summary>
        /// The eye distance in use: with GameFov framing, the distance at which the center screen's
        /// height spans the game's vertical FOV; otherwise the measured one.
        /// </summary>
        public double EyeDistance(PixelRect center, double gameFovDeg)
        {
            if (Framing.Value == CenterFraming.EyeDistance || gameFovDeg <= 1 || gameFovDeg >= 179) return EyeDistanceMm.Value;
            double centerHeightMm = Center.WidthMm.Value * center.Height / center.Width;
            return centerHeightMm / 2 / Math.Tan(gameFovDeg * Math.PI / 360);
        }

        /// <summary>The layout for the given desktop rects and the game's current vertical FOV.</summary>
        public LayoutSpec ToLayout(PixelRect left, PixelRect center, PixelRect right, double gameFovDeg) =>
            LayoutAt(left, center, right, EyeDistance(center, gameFovDeg));

        /// <summary>The screens as they physically are, with the real eye at EyeDistanceMm whatever the framing renders for.</summary>
        public LayoutSpec PhysicalLayout(PixelRect left, PixelRect center, PixelRect right) =>
            LayoutAt(left, center, right, EyeDistanceMm.Value, physical: true);

        LayoutSpec LayoutAt(PixelRect left, PixelRect center, PixelRect right, double eye, bool physical = false)
        {
            double pitch = Center.WidthMm.Value / center.Width;
            if (!Measured.Value) return Layout.DesktopPlane(left, center, right, pitch, eye);

            return new LayoutSpec
            {
                Center = Spec(Center, center, center, pitch, physical),
                Left = { Spec(Left, left, center, pitch, physical) },
                Right = { Spec(Right, right, center, pitch, physical) },
                EyeDistanceMm = eye,
                EyeHeightMm = EyeHeightMm.Value,
                EyeSideMm = EyeSideMm.Value,
            };
        }

        /// <summary>
        /// How far the band sits above (+) or below (−) level on a side screen, in mm. The Windows
        /// arrangement decides which of the screen's rows the window lights, and VerticalOffsetMm
        /// says where the screen really is. Null when the geometry takes its vertical position from
        /// the arrangement itself, which then can't be checked against anything.
        /// </summary>
        public double? BandOffsetMm(Screen s, PixelRect rect, PixelRect center)
        {
            if (s.VerticalOffsetMm == null || VerticalFromDesktop.Value || rect.Width <= 0) return null;
            return Layout.BandOffsetMm(s.VerticalOffsetMm.Value, rect, center, MmPerPixel(s, rect));
        }

        public static double MmPerPixel(Screen s, PixelRect rect) => s.WidthMm.Value / rect.Width;

        /// <summary>
        /// A side screen's real angle: PhysicalAngleDeg when it holds a number in range, otherwise
        /// AngleDeg. In game-FOV framing the tuner settles AngleDeg on an effective angle, well below
        /// the real one, so sizing anything by the real eye needs the real angle.
        /// </summary>
        public static double PhysicalAngle(Screen s)
        {
            double angle = s.AngleDeg?.Value ?? 0;
            string text = s.PhysicalAngleDeg?.Value?.Trim() ?? "";
            if (text.Length > 0 && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double real)
                && real >= AngleRange.MinValue && real <= AngleRange.MaxValue)
                angle = real;
            return angle;
        }

        ScreenSpec Spec(Screen s, PixelRect rect, PixelRect center, double centerPitch, bool physical) => new ScreenSpec
        {
            Name = s.Role,
            WidthMm = s.WidthMm.Value,
            PixelWidth = rect.Width,
            PixelHeight = rect.Height,
            AngleDeg = physical ? PhysicalAngle(s) : s.AngleDeg?.Value ?? 0,
            GapMm = s.GapMm?.Value ?? 0,
            VerticalOffsetMm = s.VerticalOffsetMm == null ? 0
                : VerticalFromDesktop.Value ? Layout.DesktopVerticalOffsetMm(rect, center, centerPitch)
                : s.VerticalOffsetMm.Value,
        };
    }
}
