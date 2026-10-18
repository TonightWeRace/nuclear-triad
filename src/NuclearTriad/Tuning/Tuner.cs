using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using NuclearTriad.Geometry;
using NuclearTriad.Rig;
using UnityEngine;

namespace NuclearTriad.Tuning
{
    /// <summary>A screen as the tuner's width step draws on it: its place in the window, its pixels and its monitor's report.</summary>
    internal struct TunerScreen
    {
        public string Role;
        /// <summary>The screen's part of the window, in GUI coordinates; empty while it isn't in the window.</summary>
        public Rect Area;
        /// <summary>The monitor's width in pixels, which with WidthMm gives the pixel pitch.</summary>
        public int PixelWidth;
        /// <summary>The picture width the monitor reports to Windows (mm); 0 when it reports none.</summary>
        public double ReportedWidthMm;
    }

    /// <summary>
    /// The on-screen tuner: a small window on the center screen that walks through the geometry
    /// one step at a time, with the tuning lines up. The screen widths come first: every other
    /// setting is in millimetres on screens whose size the widths define, so a wrong width sends
    /// the later steps to wrong values. The widths can't be judged from the lines, so the step
    /// shows each monitor's own report beside its value and draws a bar of a stated length on
    /// each screen for a tape measure. Then the eye height: every line is drawn for the eye, and a
    /// wrong eye height also bends the sloping lines that the gap and angle steps judge. Then the
    /// side screens' vertical position, gap and angle. That order follows the lines
    /// (Geometry/TuningLines.cs). The yellow line shows only the eye height and vertical position,
    /// as a bend and a step. Of the sloping lines, a gap shows near the gaps as a step, and an
    /// angle further along as a bend.
    /// Changes apply and save as they're made; Revert puts back what was there when it opened.
    /// The side screens move together unless unlinked; linked, both get the same change, so a
    /// difference between them stays.
    ///
    /// It shows the mouse cursor while open, as ConfigurationManager does. The game takes a
    /// visible cursor as a reason to pause cockpit mouse look and, with its menu weapon safety on
    /// (the default), to hold fire. Closing hands the cursor back as the game's cursor manager
    /// last left it.
    /// </summary>
    internal sealed class Tuner
    {
        const int WindowId = 0x4E54_5455;
        const float Width = 960f;
        static readonly Color Yellow = new Color(1f, 0.86f, 0f);

        sealed class Step
        {
            public string Title;
            public string Hint;
            public string Unit;
            public string Format;
            public float Fine;
            public float Coarse;
            // One value for the whole rig, one per side screen, or one per screen, all three.
            public Func<RigConfig, ConfigEntry<float>> Shared;
            public Func<RigConfig.Screen, ConfigEntry<float>> Setting;
            public bool AllScreens;
            public bool ShowsBand;
        }

        // The signs follow the lines as the geometry moves them: the hints say which way to press.
        static readonly Step[] Steps =
        {
            new Step
            {
                Title = "Screen widths", Unit = "mm", Format = "0", Fine = 1f, Coarse = 10f, Setting = s => s.WidthMm, AllScreens = true,
                Hint = "Every later step is worked out from these, so check them first, with a tape measure. Each is the width of that " +
                       "screen's picture, from the first lit pixel to the last, not the frame. Each started at what its monitor reports " +
                       "to Windows, shown beside it: usually right to a few millimetres, and a fair start if you can't measure. " +
                       "Or measure the yellow bar on each screen: if your tape reads longer than the bar says, press +; shorter, press −.",
            },
            new Step
            {
                Title = "Eye height", Unit = "mm", Format = "+0;-0;0", Fine = 2f, Coarse = 10f, Shared = c => c.EyeHeightMm,
                Hint = "Sit as you fly and look straight ahead. The yellow line and ring mark where the rig takes your eyes to be: " +
                       "move them to the height of your eyes. Then follow the yellow line out to the side screens' far edges. " +
                       "At eye height it runs on straight; if it bends down past the gaps, press +, and if it bends up, press −. " +
                       "Leave any step at the gaps for the next step.",
            },
            new Step
            {
                Title = "Vertical position", Unit = "mm", Format = "+0.0;-0.0;0.0", Fine = 0.5f, Coarse = 2f, Setting = s => s.VerticalOffsetMm,
                ShowsBand = true,
                Hint = "Watch the yellow line at each gap: it must carry on level, without a step. " +
                       "Where it sits higher on a side screen than on the center, press + for that side; where it sits lower, press −.",
            },
            new Step
            {
                Title = "Gap", Unit = "mm", Format = "0", Fine = 1f, Coarse = 5f, Setting = s => s.GapMm,
                Hint = "Watch the cyan and green lines where they cross each gap: each must come out exactly in line with where it went in. " +
                       "A wrong gap steps them opposite ways, one up and one down. If the line climbing toward the side screen steps up, " +
                       "press −; if it steps down, press +.",
            },
            new Step
            {
                Title = "Angle", Unit = "°", Format = "0.0", Fine = 0.1f, Coarse = 1f, Setting = s => s.AngleDeg,
                Hint = "The same lines must run on straight past each gap, without bending. A wrong angle bends them opposite ways, so " +
                       "the cross they make opens or closes. If the line climbing toward the side screen bends up, press −; " +
                       "if it bends down, press +.",
            },
        };

        readonly ManualLogSource log;
        readonly RigConfig config;
        readonly TuningPattern pattern;
        readonly List<KeyValuePair<ConfigEntryBase, object>> saved = new List<KeyValuePair<ConfigEntryBase, object>>();
        bool open;
        int step;
        bool linked = true;
        bool placed;
        Rect window;
        float pictureHeightMm;
        IList<TunerScreen> screens;
        bool cursorWasVisible;
        CursorLockMode cursorWasLocked;
        GUIStyle windowStyle, headingStyle, noteStyle, textStyle, smallStyle, labelStyle, valueStyle, buttonStyle, toggleStyle, rulerStyle;

        public Tuner(ManualLogSource log, RigConfig config)
        {
            this.log = log;
            this.config = config;
            pattern = new TuningPattern(log);
        }

        public bool IsOpen => open;

        /// <summary>Set when the window's Done or Close button is pressed; the caller turns the setting off.</summary>
        public bool CloseRequested { get; private set; }

        public string StepTitle => Steps[step].Title;

        public void Open()
        {
            if (open) return;
            open = true;
            step = 0;
            linked = true;
            placed = false;
            CloseRequested = false;

            saved.Clear();
            foreach (ConfigEntryBase entry in Tunable()) saved.Add(new KeyValuePair<ConfigEntryBase, object>(entry, entry.BoxedValue));
            // The tuner works on the measured geometry, with explicit vertical offsets.
            config.Measured.Value = true;
            config.VerticalFromDesktop.Value = false;

            cursorWasVisible = Cursor.visible;
            cursorWasLocked = Cursor.lockState;
            pattern.Show(true);
            log.LogInfo($"Tuner open: {Summary()}");
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            CloseRequested = false;
            pattern.Show(false);
            RestoreCursor();
            log.LogInfo($"Tuner closed: {Summary()}");
        }

        public void Revert()
        {
            foreach (KeyValuePair<ConfigEntryBase, object> entry in saved) entry.Key.BoxedValue = entry.Value;
            // Still tuning, so keep the settings the tuner needs.
            config.Measured.Value = true;
            config.VerticalFromDesktop.Value = false;
            log.LogInfo($"Tuner reverted: {Summary()}");
        }

        public void Next() => step = Math.Min(step + 1, Steps.Length - 1);

        public void Back() => step = Math.Max(step - 1, 0);

        public bool Linked
        {
            get => linked;
            set => linked = value;
        }

        /// <summary>
        /// Moves the current step's setting by delta. A shared setting has one value; a side
        /// screen's moves on both sides when side is null (linked), otherwise on the one given.
        /// A per-screen setting moves on the screen given, or on all three when none is.
        /// </summary>
        public void Nudge(float delta, RigConfig.Screen screen = null)
        {
            Step s = Steps[step];
            if (s.Shared != null)
            {
                Move(s.Shared(config), delta);
                return;
            }
            RigConfig.Screen[] all = s.AllScreens ? new[] { config.Left, config.Center, config.Right } : new[] { config.Left, config.Right };
            foreach (RigConfig.Screen target in screen == null ? all : new[] { screen })
                Move(s.Setting(target), delta);
        }

        static void Move(ConfigEntry<float> entry, float delta) => entry.Value = Mathf.Round((entry.Value + delta) * 100f) / 100f;

        public string Summary() =>
            $"widths {config.Left.WidthMm.Value:0}/{config.Center.WidthMm.Value:0}/{config.Right.WidthMm.Value:0} mm, " +
            $"eye height {config.EyeHeightMm.Value:+0;-0;0} mm, left {Describe(config.Left)}, right {Describe(config.Right)}";

        static string Describe(RigConfig.Screen s) =>
            $"angle {s.AngleDeg.Value:0.0}°, gap {s.GapMm.Value:0} mm, vertical {s.VerticalOffsetMm.Value:+0.0;-0.0;0.0} mm";

        public void Follow(Camera eye, IList<Panel> panels) => pattern.Follow(eye, panels);

        /// <summary>Keeps the cursor free while the window is up; the game hides it again on focus changes.</summary>
        public void KeepCursor()
        {
            if (!open) return;
            if (!Cursor.visible) Cursor.visible = true;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        }

        // The game's cursor manager caches whether it wants the cursor and only acts on changes, so
        // its refresh wouldn't undo ours: put back what it wants, as it would set it.
        void RestoreCursor()
        {
            try
            {
                bool wanted = CursorManager.Visible;
                Cursor.visible = wanted;
                Cursor.lockState = wanted ? CursorLockMode.None : CursorLockMode.Locked;
            }
            catch (Exception e)
            {
                Cursor.visible = cursorWasVisible;
                Cursor.lockState = cursorWasLocked;
                log.LogWarning($"Couldn't ask the game for its cursor state, so the tuner put back its own: {e.Message}");
            }
        }

        IEnumerable<ConfigEntryBase> Tunable()
        {
            yield return config.Measured;
            yield return config.VerticalFromDesktop;
            yield return config.EyeHeightMm;
            yield return config.Center.WidthMm;
            foreach (RigConfig.Screen s in new[] { config.Left, config.Right })
            {
                yield return s.WidthMm;
                yield return s.VerticalOffsetMm;
                yield return s.AngleDeg;
                yield return s.GapMm;
            }
        }

        RigConfig.Screen ScreenFor(string role) => role == "left" ? config.Left : role == "right" ? config.Right : config.Center;

        /// <summary>
        /// Draws the window on the center screen, given in GUI coordinates. centerMonitor is the
        /// center screen's desktop rect, null while the screens aren't found. note says what stops
        /// the rig, or puts it on the wrong screens; screens names the display each screen uses.
        /// Any of the three strings may be null. rulers gives each screen's place in the window and
        /// its monitor's report, for the width step; null while the screens aren't found.
        /// </summary>
        public void OnGUI(Rect centerScreen, PixelRect? centerMonitor, string note, string screens, string bandNote, IList<TunerScreen> rulers)
        {
            if (!open) return;
            KeepCursor();
            EnsureStyles();
            this.screens = rulers;
            // The center screen's picture height, from its width and its monitor's pixel aspect. Not
            // known while the screens aren't found, when centerScreen falls back to the whole window.
            pictureHeightMm = centerMonitor is PixelRect m && m.Width > 0 ? config.Center.WidthMm.Value * m.Height / m.Width : 0f;
            if (Steps[step].AllScreens && rulers != null) DrawRulers(rulers);
            if (!placed)
            {
                float width = Mathf.Min(Width, centerScreen.width - 40f);
                window = new Rect(centerScreen.x + (centerScreen.width - width) / 2, centerScreen.yMax - 620f, width, 0f);
                placed = true;
            }
            window = GUILayout.Window(WindowId, window, id => DrawWindow(note, screens, bandNote), "Nuclear Triad screen tuner", windowStyle,
                GUILayout.Width(window.width));
            // A long note can push the window's foot off the center screen: lift it back on.
            float bottom = centerScreen.yMax - 20f;
            if (window.yMax > bottom) window.y = Mathf.Max(centerScreen.y + 20f, bottom - window.height);
        }

        // A bar of a stated length on each screen, drawn at the pixel pitch the screen's width
        // gives, so a tape measure checks the width without finding the picture's edge pixels. In
        // the upper part of the screen, clear of the tuner's window and most cockpits' panels.
        void DrawRulers(IList<TunerScreen> rulers)
        {
            foreach (TunerScreen screen in rulers)
            {
                if (screen.Area.width <= 0 || screen.PixelWidth <= 0) continue;
                float widthMm = ScreenFor(screen.Role).WidthMm.Value;
                if (widthMm <= 0) continue;
                double mm = ScreenSize.RulerMm(widthMm);
                float length = Mathf.Min((float)(mm * screen.PixelWidth / widthMm), screen.Area.width - 40f);
                float x = screen.Area.x + (screen.Area.width - length) / 2, y = screen.Area.y + screen.Area.height * 0.28f;

                Color was = GUI.color;
                GUI.color = Yellow;
                GUI.DrawTexture(new Rect(x, y - 3f, length, 6f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x - 1f, y - 24f, 3f, 48f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x + length - 2f, y - 24f, 3f, 48f), Texture2D.whiteTexture);
                GUI.color = was;

                var label = new GUIContent($"{mm:0} mm, if this screen's width is right");
                Vector2 size = rulerStyle.CalcSize(label);
                GUI.Box(new Rect(x + (length - size.x) / 2, y - 36f - size.y, size.x, size.y), label, rulerStyle);
            }
        }

        void DrawWindow(string note, string screensLine, string bandNote)
        {
            Step s = Steps[step];
            GUILayout.Label($"Step {step + 1} of {Steps.Length}: {s.Title}", headingStyle);
            // No step works until the rig is up, and on the right screens, so what stops that comes
            // first, in the notice's amber.
            if (note != null) GUILayout.Label(note, noteStyle);
            GUILayout.Label("Park first and look straight ahead: these lines move with your view. Changes apply and save as you go.", smallStyle);
            if (screensLine != null) GUILayout.Label(screensLine, smallStyle);
            GUILayout.Space(6);
            GUILayout.Label(s.Hint, textStyle);
            GUILayout.Space(8);

            if (s.Shared != null)
            {
                ConfigEntry<float> entry = s.Shared(config);
                Row(s, "Your eyes", Value(s, entry), 340, d => Nudge(d));
                if (pictureHeightMm > 0)
                    GUILayout.Label($"That's {entry.Value + pictureHeightMm / 2:0} mm above the bottom edge of the center screen's picture, " +
                                    "if you'd rather measure.", smallStyle);
            }
            else if (s.AllScreens)
            {
                foreach (RigConfig.Screen screen in new[] { config.Left, config.Center, config.Right }) WidthRow(s, screen);
            }
            else
            {
                linked = GUILayout.Toggle(linked, "  Move both side screens together", toggleStyle);
                if (linked) Row(s, "Both sides", $"L {Value(s, s.Setting(config.Left))}   R {Value(s, s.Setting(config.Right))}", 340, d => Nudge(d));
                else
                {
                    Row(s, "Left", Value(s, s.Setting(config.Left)), 200, d => Nudge(d, config.Left));
                    Row(s, "Right", Value(s, s.Setting(config.Right)), 200, d => Nudge(d, config.Right));
                }
            }
            if (s.ShowsBand && bandNote != null)
                GUILayout.Label($"Separately, the band Windows lights on the side screens: {bandNote}.", smallStyle);

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUI.enabled = step > 0;
            if (GUILayout.Button("Back", buttonStyle, GUILayout.Width(120))) Back();
            GUI.enabled = true;
            if (step < Steps.Length - 1)
            {
                if (GUILayout.Button("Next", buttonStyle, GUILayout.Width(120))) Next();
            }
            else if (GUILayout.Button("Done", buttonStyle, GUILayout.Width(120))) CloseRequested = true;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Revert", buttonStyle, GUILayout.Width(120))) Revert();
            if (GUILayout.Button("Close", buttonStyle, GUILayout.Width(120))) CloseRequested = true;
            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }

        void Row(Step s, string label, string value, float valueWidth, Action<float> nudge)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(150));
            foreach (float d in new[] { -s.Coarse, -s.Fine })
                if (GUILayout.Button(Delta(d), buttonStyle, GUILayout.Width(80))) nudge(d);
            GUILayout.Label(value, valueStyle, GUILayout.Width(valueWidth));
            foreach (float d in new[] { s.Fine, s.Coarse })
                if (GUILayout.Button(Delta(d), buttonStyle, GUILayout.Width(80))) nudge(d);
            GUILayout.EndHorizontal();
        }

        // One screen's width, with what its monitor reports beside it and a button to take that.
        void WidthRow(Step s, RigConfig.Screen screen)
        {
            ConfigEntry<float> entry = s.Setting(screen);
            double reported = Reported(screen.Role);
            GUILayout.BeginHorizontal();
            GUILayout.Label(char.ToUpperInvariant(screen.Role[0]) + screen.Role.Substring(1), labelStyle, GUILayout.Width(100));
            foreach (float d in new[] { -s.Coarse, -s.Fine })
                if (GUILayout.Button(Delta(d), buttonStyle, GUILayout.Width(70))) Nudge(d, screen);
            GUILayout.Label(Value(s, entry), valueStyle, GUILayout.Width(150));
            foreach (float d in new[] { s.Fine, s.Coarse })
                if (GUILayout.Button(Delta(d), buttonStyle, GUILayout.Width(70))) Nudge(d, screen);
            GUILayout.Space(12);
            if (reported <= 0) GUILayout.Label("the monitor reports no size", smallStyle);
            else if (Mathf.Abs(entry.Value - (float)reported) < 0.5f) GUILayout.Label("as the monitor reports", smallStyle);
            else
            {
                GUILayout.Label($"the monitor reports {reported:0} mm", smallStyle, GUILayout.ExpandWidth(false));
                if (GUILayout.Button("Use it", buttonStyle, GUILayout.Width(90))) entry.Value = (float)Math.Round(reported);
            }
            GUILayout.EndHorizontal();
        }

        double Reported(string role)
        {
            if (screens == null) return 0;
            foreach (TunerScreen screen in screens)
                if (screen.Role == role) return screen.ReportedWidthMm;
            return 0;
        }

        static string Value(Step s, ConfigEntry<float> entry) => entry.Value.ToString(s.Format) + " " + s.Unit;

        static string Delta(float d) => (d > 0 ? "+" : "−") + Math.Abs(d).ToString("0.#");

        void EnsureStyles()
        {
            if (windowStyle != null) return;
            windowStyle = new GUIStyle(GUI.skin.window) { fontSize = 20, padding = new RectOffset(18, 18, 40, 16) };
            headingStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, wordWrap = true };
            noteStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, wordWrap = true };
            noteStyle.normal.textColor = new Color(1f, 0.85f, 0.45f);
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, wordWrap = true };
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
            valueStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 20, fixedHeight = 42 };
            toggleStyle = new GUIStyle(GUI.skin.toggle) { fontSize = 20 };
            rulerStyle = new GUIStyle(GUI.skin.box) { fontSize = 22, fontStyle = FontStyle.Bold, padding = new RectOffset(14, 14, 8, 8) };
            rulerStyle.normal.textColor = Yellow;
        }
    }
}
