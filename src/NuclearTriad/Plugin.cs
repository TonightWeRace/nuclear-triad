using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearTriad.Dev;
using NuclearTriad.Geometry;
using NuclearTriad.Patches;
using NuclearTriad.Rig;
using NuclearTriad.Tuning;
using UnityEngine;
using UnityEngine.Rendering;

namespace NuclearTriad
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("NuclearOption.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nuclear-triad";
        public const string Name = "Nuclear Triad";
        public const string Version = "0.1.29";

        internal static ManualLogSource Log { get; private set; }

        ConfigEntry<bool> commandChannelEnabled;
        ConfigEntry<string> outputDir;
        CommandChannel commands;
        RigConfig geometry;
        TriadRig rig;
        WindowSpan span;
        Curtains curtains;
        SkyGrid grid;
        Tuner tuner;
        HudBender bender;
        EyeHold eyeHold;
        StraightAhead straightAhead;
        MenuPinning menus;
        HudCorners corners;
        CaptureTool capture;
        FrameTimer timing;

        bool rigActive;
        bool offAfterError;
        IntPtr window;
        float nextScreenRefresh;
        bool haveScreens;
        bool warnedScreens;
        string screensNotFound;
        string screensText;
        string screensOutOfOrder;
        string lastScreens;
        PixelRect leftRect, centerRect, rightRect;
        RigConfig.Pick leftPick, centerPick, rightPick;
        bool pollingAnnounced;
        bool quitting;
        string lastBandArrangement;
        GUIStyle noticeStyle;

        void Awake()
        {
            Log = Logger;
            VersionGuard.LogAndCheck(Logger, Paths.GameRootPath);

            geometry = new RigConfig(Config);
            // Before the game's first scene, which is where it would remove the object every plugin sits on.
            BepInExSetup.Apply(Logger, gameObject, geometry.FixBepInExManager.Value);
            // After the rig's settings, so F1 lists the Dev section last. Off unless a cfg turns it on,
            // as tools\install-mods.ps1 does for the dev machine.
            commandChannelEnabled = Config.Bind("Dev", "CommandChannel", false, new ConfigDescription(
                "Run the commands in BepInEx/config/nuclear-triad.cmd, checked once a second. For development.", null,
                new ConfigurationManagerAttributes { IsAdvanced = true }));
            outputDir = Config.Bind("Dev", "OutputDir", "", new ConfigDescription(
                "Folder for captures and dumps. Empty: BepInEx/nuclear-triad in the game folder.", null,
                new ConfigurationManagerAttributes { IsAdvanced = true }));
            rig = new TriadRig(Logger, geometry);
            span = new WindowSpan(Logger, geometry.OriginalScreen);
            grid = new SkyGrid(Logger);
            tuner = new Tuner(Logger, geometry);
            bender = new HudBender(Logger, rig, geometry);
            eyeHold = new EyeHold(Logger, rig, geometry);
            straightAhead = new StraightAhead(Logger, rig, geometry);
            menus = new MenuPinning(Logger);
            corners = new HudCorners(Logger);
            // Early in the frame the HUD bend is undone; late, after the camera moves, the eye is
            // held and then the HUD is placed and bent, in that order.
            gameObject.AddComponent<EarlyDriver>().Tick = bender.Early;
            gameObject.AddComponent<LateDriver>().Tick = () =>
            {
                eyeHold.Late();
                bender.Late();
                straightAhead.Late();
            };
            capture = new CaptureTool(this, Logger);
            timing = new FrameTimer(Logger);
            rigActive = geometry.RigEnabled.Value;
            // Turning Rig.Enabled on switches the rig on too, so the setting alone brings it up, even
            // when it was off at startup or the hotkey has switched the rig off since.
            geometry.RigEnabled.SettingChanged += (sender, e) =>
            {
                if (geometry.RigEnabled.Value) Logger.LogInfo($"Rig.Enabled turned on, so {Toggle("on")}");
            };
            Config.SettingChanged += (sender, e) => nextScreenRefresh = 0;
            // The HUD markers' patch always goes in; the setting decides whether the rig answers.
            try
            {
                Harmony.CreateAndPatchAll(typeof(MainCameraScreenPointPatch), Guid);
            }
            catch (Exception e)
            {
                Logger.LogError($"Couldn't patch the main camera's screen projection, so HUD markers keep the game's placement: {e}");
            }
            ApplyHudHook();
            geometry.HudSideMarkers.SettingChanged += (sender, e) => ApplyHudHook();
            // Likewise the vegetation cull's patch, behind its own setting.
            try
            {
                Harmony.CreateAndPatchAll(typeof(VegetationCullPatch), Guid);
            }
            catch (Exception e)
            {
                Logger.LogError($"Couldn't patch the vegetation cull, so trees and grass keep the game's cull: {e}");
            }
            ApplyVegetationHook();
            geometry.VegetationAllScreens.SettingChanged += (sender, e) => ApplyVegetationHook();
            // And the off-screen arrows' patch, behind Hud.FollowStraightAhead.
            try
            {
                Harmony.CreateAndPatchAll(typeof(OffScreenArrowPatch), Guid);
                Harmony.CreateAndPatchAll(typeof(ObjectiveArrowPatch), Guid);
            }
            catch (Exception e)
            {
                Logger.LogError($"Couldn't patch the off-screen arrows, so some keep to the window's middle: {e}");
            }
            ApplyArrowHook();
            geometry.HudFollowStraightAhead.SettingChanged += (sender, e) => ApplyArrowHook();
            // F1's checkbox, the hotkey and the 'grid' command all go through the setting, which
            // starts off every time so no one inherits the grid from a tuning session.
            geometry.SkyGrid.Value = false;
            geometry.SkyGrid.SettingChanged += (sender, e) => Logger.LogInfo(grid.Show(geometry.SkyGrid.Value));
            // The tuner works the same way, and starts closed.
            geometry.Tuner.Value = false;
            geometry.Tuner.SettingChanged += (sender, e) =>
            {
                if (geometry.Tuner.Value) tuner.Open();
                else tuner.Close();
            };

            commands = new CommandChannel(Logger, Paths.ConfigPath);
            commands.Register("ping", args => "pong");
            commands.Register("help", args => string.Join(", ", commands.Commands));
            commands.Register("version", args => VersionGuard.Describe());
            commands.Register("dump-cameras", args => CameraDump.Write(OutputDirectory, Label(args)));
            commands.Register("dump-volumes", args => VolumeDump.Write(OutputDirectory, Label(args)));
            commands.Register("dump-canvases", args => CanvasDump.Write(OutputDirectory, Label(args)));
            commands.Register("dump-ui", args => UiDump.Write(OutputDirectory, Label(args)));
            commands.Register("dump-displays", args => DisplayDump.Write(OutputDirectory, Label(args)));
            commands.Register("dump-geometry", args => GeometryDump.Write(geometry, OutputDirectory, Label(args), GameFov()));
            commands.Register("toggle", args => Toggle(args.Length > 0 ? args[0] : null));
            commands.Register("span", SpanCommand);
            commands.Register("capture", args => DoCapture(Label(args)));
            commands.Register("grid", args =>
            {
                geometry.SkyGrid.Value = args.Length == 0 ? !geometry.SkyGrid.Value : !string.Equals(args[0], "off", StringComparison.OrdinalIgnoreCase);
                return $"sky grid {(grid.Visible ? "on" : "off")}";
            });
            commands.Register("tune", TuneCommand);
            commands.Register("hud-check", args => HudCheck.Run(rig));
            commands.Register("dump-hud", args => HudDump.Write(OutputDirectory, Label(args)));
            commands.Register("timing", TimingCommand);
            commands.Register("rig-status", args => Status());
            commands.Register("get", args => Describe(FindSetting(args)));
            commands.Register("set", args =>
            {
                ConfigEntryBase entry = FindSetting(args);
                string text = string.Join(" ", args.Skip(1).ToArray());
                // Parsed here, not by SetSerializedValue, which drops a bad value with only a log line.
                object asked = TomlTypeConverter.ConvertToValue(text, entry.SettingType);
                entry.BoxedValue = asked;
                AcceptableValueBase range = entry.Description.AcceptableValues;
                return range == null || Equals(asked, entry.BoxedValue)
                    ? Describe(entry)
                    : $"{Describe(entry)} (asked for {text}; {range.ToDescriptionString().TrimStart('#', ' ')})";
            });
            commands.Register("reload-config", args =>
            {
                Config.Reload();
                nextScreenRefresh = 0;
                return "config reloaded";
            });

            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            Application.quitting += () =>
            {
                quitting = true;
                span.OnQuitting();
            };
        }

        void Update()
        {
            if (commandChannelEnabled.Value)
            {
                if (!pollingAnnounced)
                {
                    pollingAnnounced = true;
                    Logger.LogInfo($"Control channel polling {Path.Combine(Paths.ConfigPath, "nuclear-triad.cmd")}");
                }
                commands.Poll();
            }
            Hotkeys();
            tuner.KeepCursor();

            if (!Win32.IsAlive(window)) window = Win32.GameWindow();
            RefreshScreens();
            span.Update(window);
            // Menus, and the HUD's corner panels, to the center screen whenever the window spans
            // several, in missions or not.
            ViewportMapping? centerShare = haveScreens && span.IsSpanned && Win32.IsAlive(window)
                ? Viewport.Map(centerRect, Win32.ClientRectOnScreen(window)) : (ViewportMapping?)null;
            menus.Update(geometry.MenusOnCenter.Value, centerShare);
            corners.Update(geometry.HudCornerPanels.Value == CornerPlacement.CenterScreen, centerShare);
            rig.Track(rigActive && geometry.RigEnabled.Value && haveScreens);
            timing.Tick(State);
            capture.Drain();
        }

        // Before any camera renders: the rig lays out every camera, and the grid and tuning lines follow the eye.
        void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            try
            {
                if (rig.Built && haveScreens && Win32.IsAlive(window))
                    rig.ApplyFrame(Win32.ClientRectOnScreen(window), leftRect, centerRect, rightRect);
                Camera eye = rig.Main != null ? rig.Main : Camera.main;
                grid.Follow(eye);
                tuner.Follow(eye, rig.Built ? rig.CurrentPanels : null);
            }
            catch (Exception e)
            {
                // Fail safe: an unexpected error switches the rig off rather than leaving the game
                // unplayable. Geometry settings the rig can't render don't land here; it keeps its
                // last layout instead.
                rigActive = false;
                offAfterError = true;
                Logger.LogError($"Rig layout failed, so the rig is off; {geometry.ToggleKey.Value} or 'toggle on' tries again. {e}");
            }
        }

        void OnGUI()
        {
            string rejected = rig.Rejected;
            if (rejected == null && !tuner.IsOpen) return;
            Rect area = CenterScreenArea();
            if (rejected != null) DrawNotice(area, rejected);
            if (tuner.IsOpen)
            {
                // Screens out of order come first: the rig may be up, but on the wrong screens.
                string note = string.Join(" ", new[] { screensOutOfOrder, rig.Built ? null : RigOffNote() }.Where(n => n != null).ToArray());
                tuner.OnGUI(area, haveScreens ? centerRect : (PixelRect?)null, note.Length > 0 ? note : null,
                            screensText == null ? null : $"Screens: {screensText}", BandReport(), haveScreens ? TunerScreens() : null);
                if (tuner.CloseRequested) geometry.Tuner.Value = false;
            }
        }

        // Why the rig isn't built, and what to do about it, for the tuner's window. Every reason
        // that holds is named, setup first, since each needs its own fix.
        string RigOffNote()
        {
            var why = new List<string>();
            if (screensNotFound != null) why.Add(screensNotFound);
            // Turning Rig.Enabled on switches the rig on too, so the switch only matters while it's on.
            if (!geometry.RigEnabled.Value) why.Add("Rig.Enabled is off: turn it on in F1 (tick Advanced settings to see it).");
            else if (!rigActive)
                why.Add(offAfterError
                    ? $"An error switched the rig off (BepInEx's log has it): {geometry.ToggleKey.Value} tries again."
                    : $"The rig is switched off: {geometry.ToggleKey.Value} switches it on.");
            if (!rig.InMission) why.Add("The rig comes up in a mission: start one.");
            else if (rig.OutsideCockpit) why.Add("Rig.CockpitOnly is on, so the rig comes up only in the cockpit view: switch to it.");
            return why.Count > 0 ? string.Join(" ", why) : null;
        }

        // The center screen in GUI coordinates (origin top left), from its desktop rect, so it's
        // known even before any layout has worked; the whole window while the screens aren't found.
        Rect CenterScreenArea()
        {
            Rect area = haveScreens ? ScreenArea(centerRect) : default(Rect);
            return area.width > 0 ? area : new Rect(0, 0, Screen.width, Screen.height);
        }

        // A screen's part of the window in GUI coordinates; empty while the window doesn't reach it.
        Rect ScreenArea(PixelRect screen)
        {
            ViewportMapping c = Win32.IsAlive(window) ? Viewport.Map(screen, Win32.ClientRectOnScreen(window)) : default(ViewportMapping);
            return c.IsEmpty
                ? default(Rect)
                : new Rect((float)(c.X * Screen.width), (float)((1 - c.Y - c.Height) * Screen.height),
                           (float)(c.Width * Screen.width), (float)(c.Height * Screen.height));
        }

        // Each screen as the tuner's width step draws on it.
        TunerScreen[] TunerScreens() => new[]
        {
            new TunerScreen { Role = "left", Area = ScreenArea(leftRect), PixelWidth = leftRect.Width, ReportedWidthMm = leftPick.ReportedWidthMm },
            new TunerScreen { Role = "center", Area = ScreenArea(centerRect), PixelWidth = centerRect.Width, ReportedWidthMm = centerPick.ReportedWidthMm },
            new TunerScreen { Role = "right", Area = ScreenArea(rightRect), PixelWidth = rightRect.Width, ReportedWidthMm = rightPick.ReportedWidthMm },
        };

        // While the geometry settings can't be rendered, say so on the center screen. Otherwise a
        // value typed into F1 that the rig can't use looks as if the rig had stopped listening.
        // ConfigurationManager opens its window centered and 650 px wide, where it would hide a
        // centered notice, so the notice sits at the center screen's top left, beside that window.
        void DrawNotice(Rect area, string rejected)
        {
            if (noticeStyle == null)
            {
                noticeStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 22, wordWrap = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(16, 16, 10, 10),
                };
                noticeStyle.normal.textColor = new Color(1f, 0.85f, 0.45f);
            }
            const float margin = 40f, configWindowWidth = 650f;
            float width = Mathf.Clamp((Screen.width - configWindowWidth) / 2 - area.x - 1.5f * margin, 300f, 900f);
            var content = new GUIContent($"Nuclear Triad can't draw these geometry settings: {rejected}. " +
                (rig.HasLayout ? "Showing the last layout that worked until they change." : "Showing the game's own view until they change."));
            GUI.Box(new Rect(area.x + margin, area.y + 60f, width, noticeStyle.CalcHeight(content, width)), content, noticeStyle);
        }

        void Hotkeys()
        {
            if (geometry.ToggleKey.Value.IsDown()) Logger.LogInfo(Toggle(null));
            if (geometry.CaptureKey.Value.IsDown()) Logger.LogInfo(DoCapture("hotkey"));
            if (geometry.GridKey.Value.IsDown()) geometry.SkyGrid.Value = !geometry.SkyGrid.Value;
            if (geometry.TunerKey.Value.IsDown()) geometry.Tuner.Value = !geometry.Tuner.Value;
        }

        // Re-reads the screens every couple of seconds, or straight after a config change, and keeps
        // the window span and curtains in step with them.
        void RefreshScreens()
        {
            if (Time.unscaledTime < nextScreenRefresh) return;
            nextScreenRefresh = Time.unscaledTime + 2f;

            List<Win32.Monitor> monitors = Win32.Monitors();
            RigConfig.Pick left = geometry.Resolve(geometry.Left, monitors);
            RigConfig.Pick center = geometry.Resolve(geometry.Center, monitors);
            RigConfig.Pick right = geometry.Resolve(geometry.Right, monitors);
            leftPick = left;
            centerPick = center;
            rightPick = right;
            haveScreens = left.Rect.HasValue && center.Rect.HasValue && right.Rect.HasValue;
            screensText = ScreensText(left, center, right);
            screensOutOfOrder = haveScreens ? OutOfOrder(left, center, right) : null;
            // Logged when the screens change, with a warning while they're out of order.
            string screens = $"{screensText} {screensOutOfOrder}";
            if (screens != lastScreens)
            {
                lastScreens = screens;
                Logger.LogInfo($"Screens: {screensText}");
                if (screensOutOfOrder != null) Logger.LogWarning(screensOutOfOrder);
            }
            if (!haveScreens)
            {
                if (!warnedScreens)
                    Logger.LogWarning($"Rig screens not found (left {left.Rect}, center {center.Rect}, right {right.Rect}); known monitors: " +
                                      string.Join(", ", monitors.Select(m => $"{m.Device} {m.Bounds}{(m.WidthMm > 0 ? $" {m.WidthMm:0} mm wide" : "")}")));
                warnedScreens = true;
                screensNotFound = ScreensNotFound(monitors);
                return;
            }
            warnedScreens = false;
            screensNotFound = null;
            leftRect = left.Rect.Value;
            centerRect = center.Rect.Value;
            rightRect = right.Rect.Value;

            span.Request(geometry.WindowRect(leftRect, centerRect, rightRect));
            UpdateCurtains();

            // Logged when the Windows arrangement changes, not on every tuning nudge.
            string arrangement = $"{leftRect} {centerRect} {rightRect} {geometry.Span.Value}";
            if (arrangement != lastBandArrangement && BandReport() is string band)
            {
                Logger.LogInfo($"Band against level: {band}");
                lastBandArrangement = arrangement;
            }
        }

        // For the tuner's note: the Monitor settings no Windows display matches, or that are blank
        // when Windows' arrangement has no row of three to follow, and the displays Windows has,
        // left to right as arranged, named as the setting takes them.
        string ScreensNotFound(List<Win32.Monitor> monitors)
        {
            var named = new List<string>();
            var blank = new List<string>();
            foreach (RigConfig.Screen s in geometry.Screens)
            {
                RigConfig.Pick pick = geometry.Resolve(s, monitors);
                if (pick.Rect.HasValue) continue;
                string setting = $"{s.Monitor.Definition.Section}.Monitor";
                if (pick.FromArrangement) blank.Add(setting);
                else named.Add($"{setting} ({s.Monitor.Value.Trim()})");
            }
            var why = new List<string>();
            if (named.Count > 0) why.Add($"No Windows display matches {Listed(named, "or")}.");
            if (blank.Count > 0)
                why.Add($"{Listed(blank, "and")} {(blank.Count > 1 ? "are blank, so they follow" : "is blank, so it follows")} Windows' " +
                        "display arrangement, but that doesn't have exactly three displays side by side.");
            string displays = string.Join(", ", monitors.OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top)
                .Select(m => RigConfig.DeviceKey(m.Device) + (m.Primary ? " (main)" : "")));
            return $"{string.Join(" ", why.ToArray())} Set {(named.Count + blank.Count > 1 ? "each" : "it")} in F1 to one Windows has, " +
                   $"left to right: {displays}.";
        }

        static string Listed(List<string> items, string conjunction) =>
            items.Count > 1 ? string.Join(", ", items.Take(items.Count - 1).ToArray()) + $" {conjunction} " + items.Last() : string.Join("", items.ToArray());

        // For the tuner, the log and rig-status: which display each screen uses, and where that came from.
        static string ScreensText(RigConfig.Pick left, RigConfig.Pick center, RigConfig.Pick right)
        {
            var picks = new[] { new KeyValuePair<string, RigConfig.Pick>("left", left), new KeyValuePair<string, RigConfig.Pick>("center", center),
                                new KeyValuePair<string, RigConfig.Pick>("right", right) };
            bool all = picks.All(p => p.Value.FromArrangement), none = picks.All(p => !p.Value.FromArrangement);
            string Name(RigConfig.Pick p)
            {
                if (p.FromPixelRect) return $"PixelRect {p.Rect}";
                if (p.Display == null) return "not found";
                var notes = new List<string>();
                if (p.Primary) notes.Add("main");
                if (!all && !none && p.FromArrangement) notes.Add("from Windows' arrangement");
                return notes.Count == 0 ? p.Display : $"{p.Display} ({string.Join(", ", notes.ToArray())})";
            }
            bool anyFound = picks.Any(p => p.Value.Rect.HasValue);
            return string.Join(", ", picks.Select(p => $"{p.Key} {Name(p.Value)}").ToArray()) +
                   (anyFound && all ? ", from Windows' display arrangement." : anyFound && none ? ", as set in F1." : ".");
        }

        // With every screen found: two screens on one display, or the three out of left-to-right
        // order in Windows' arrangement. The rig still builds, but each view goes to the display
        // named for it, which is then likely the wrong screen. Null when all's well.
        static string OutOfOrder(RigConfig.Pick left, RigConfig.Pick center, RigConfig.Pick right)
        {
            PixelRect l = left.Rect.Value, c = center.Rect.Value, r = right.Rect.Value;
            if (DisplayRow.InOrder(l, c, r)) return null;
            if (left.FromPixelRect || center.FromPixelRect || right.FromPixelRect)
                return "The screens are out of order: in Windows' display arrangement, Left must be left of Center, and Center left of " +
                       "Right. Check their Monitor and PixelRect settings in F1.";
            var picks = new[] { new KeyValuePair<string, RigConfig.Pick>("Left", left), new KeyValuePair<string, RigConfig.Pick>("Center", center),
                                new KeyValuePair<string, RigConfig.Pick>("Right", right) };
            foreach (IGrouping<string, KeyValuePair<string, RigConfig.Pick>> shared in picks.GroupBy(p => p.Value.Display))
                if (shared.Count() > 1)
                    return $"{Listed(shared.Select(p => p.Key).ToList(), "and")} {(shared.Count() > 2 ? "all" : "both")} use {shared.Key}. " +
                           "Set each screen's Monitor in F1 to a display of its own, or clear the three to follow Windows' arrangement.";
            string windows = string.Join(", ", picks.Select(p => p.Value).OrderBy(p => p.Rect.Value.Left + p.Rect.Value.Right)
                .Select(p => p.Display).ToArray());
            return $"The screens are out of order: Windows has {windows} from left to right, but the rig puts {left.Display} on the left, " +
                   $"{center.Display} in the center and {right.Display} on the right. If the views are on the wrong screens, clear the " +
                   "three Monitor settings in F1 to follow Windows' arrangement. If they're right, arrange the displays in Windows' " +
                   "display settings to match the screens.";
        }

        // In center-band mode, where the band sits on each side screen against level. The Windows
        // arrangement decides which rows the window lights; VerticalOffsetMm says where the screen
        // really is. Null when there's no band, or nothing to check it against.
        string BandReport()
        {
            if (!haveScreens || geometry.Span.Value != SpanMode.CenterBand) return null;
            string left = BandReport(geometry.Left, leftRect), right = BandReport(geometry.Right, rightRect);
            return left != null && right != null ? $"{left}; {right}" : null;
        }

        string BandReport(RigConfig.Screen screen, PixelRect rect)
        {
            double? mm = geometry.BandOffsetMm(screen, rect, centerRect);
            if (mm == null) return null;
            int px = (int)Math.Round(mm.Value / RigConfig.MmPerPixel(screen, rect));
            if (px == 0) return $"{screen.Role} level";
            return $"{screen.Role} {Math.Abs(mm.Value):F1} mm {(px > 0 ? "high" : "low")} (move that screen {Math.Abs(px)} px " +
                   $"{(px > 0 ? "up" : "down")} in Windows' display settings)";
        }

        void UpdateCurtains()
        {
            var wanted = new List<PixelRect>();
            if (geometry.Curtains.Value && span.Target.HasValue && span.IsSpanned)
                wanted = Curtains.Uncovered(new[] { leftRect, centerRect, rightRect }, span.Target.Value);
            if (curtains != null && curtains.Rects.SequenceEqual(wanted)) return;
            if (curtains == null && wanted.Count == 0) return;

            curtains?.Dispose();
            curtains = null;
            if (wanted.Count == 0) return;
            curtains = new Curtains(wanted);
            Logger.LogInfo($"Curtains over {curtains.Created} strips: {string.Join(", ", wanted)}");
        }

        string Toggle(string arg)
        {
            rigActive = arg == null ? !rigActive : string.Equals(arg, "on", StringComparison.OrdinalIgnoreCase);
            offAfterError = false;
            return $"rig {(rigActive ? "on" : "off")}";
        }

        string SpanCommand(string[] args)
        {
            if (args.Length > 0)
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "band": geometry.Span.Value = SpanMode.CenterBand; break;
                    case "full": geometry.Span.Value = SpanMode.FullHeight; break;
                    case "explicit": geometry.Span.Value = SpanMode.Explicit; break;
                    case "off": geometry.Span.Value = SpanMode.Off; break;
                    default: throw new ArgumentException("span takes band, full, explicit or off");
                }
            }
            span.Retry();
            nextScreenRefresh = 0;
            return $"span {geometry.Span.Value}";
        }

        void ApplyVegetationHook() =>
            VegetationCullPatch.Hook = geometry.VegetationAllScreens.Value ? rig.WidenVegetationCull : (Action<Camera>)null;

        void ApplyHudHook() => MainCameraScreenPointPatch.Hook = geometry.HudSideMarkers.Value ? rig.ScreenPoint : (ScreenPointHook)null;

        void ApplyArrowHook()
        {
            bool on = geometry.HudFollowStraightAhead.Value;
            OffScreenArrowPatch.Hook = on ? straightAhead.Pin : (EdgePinHook)null;
            ObjectiveArrowPatch.Hook = on ? straightAhead.PlaceObjective : (ObjectiveMarkerHook)null;
        }

        string TuneCommand(string[] args)
        {
            switch (args.Length > 0 ? args[0].ToLowerInvariant() : "")
            {
                case "": geometry.Tuner.Value = !geometry.Tuner.Value; break;
                case "on": geometry.Tuner.Value = true; break;
                case "off": geometry.Tuner.Value = false; break;
                case "next": tuner.Next(); break;
                case "back": tuner.Back(); break;
                case "revert": tuner.Revert(); break;
                case "linked": tuner.Linked = true; break;
                case "unlinked": tuner.Linked = false; break;
                default: throw new ArgumentException("tune takes on, off, next, back, revert, linked or unlinked");
            }
            return tuner.IsOpen ? $"tuner open at {tuner.StepTitle}, {(tuner.Linked ? "linked" : "unlinked")}: {tuner.Summary()}" : "tuner closed";
        }

        string TimingCommand(string[] args)
        {
            string verb = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (verb == "start") return timing.Start(OutputDirectory, args.Length > 1 && args[1].ToLowerInvariant() == "uncapped");
            if (verb == "stop") return timing.Stop();
            throw new ArgumentException("timing takes start [uncapped] or stop");
        }

        string DoCapture(string label)
        {
            PixelRect? client = Win32.IsAlive(window) ? Win32.ClientRectOnScreen(window) : (PixelRect?)null;
            var seams = new List<int>();
            if (client.HasValue && haveScreens)
            {
                ViewportMapping center = Viewport.Map(centerRect, client.Value);
                foreach (double x in new[] { center.X, center.X + center.Width })
                {
                    int px = (int)Math.Round(x * Screen.width);
                    if (!center.IsEmpty && px > 0 && px < Screen.width) seams.Add(px);
                }
            }

            Camera eye = rig.Main != null ? rig.Main : Camera.main;
            int horizonY = -1;
            if (eye != null)
            {
                Vector3 level = Vector3.ProjectOnPlane(eye.transform.forward, Vector3.up);
                if (level.sqrMagnitude > 1e-4f)
                {
                    Vector3 p = eye.WorldToScreenPoint(eye.transform.position + level.normalized * 10000f);
                    if (p.z > 0) horizonY = (int)Math.Round(p.y);
                }
            }

            // With all three views up, add eye-space views centered on each seam at the same rows as
            // the raw crops: the check that holds for toed-in screens.
            List<WindowView> views = null;
            var eyeSeams = new List<CaptureTool.EyeSeam>();
            if (rig.Built && rig.Views.Count == 3)
            {
                views = rig.Views.Select(v => WindowView.From(v.Name, v.Panel, v.Viewport, Screen.width, Screen.height)).ToList();
                WindowView center = views[1];
                var rows = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>("mid", Screen.height / 2) };
                if (horizonY >= 0) rows.Insert(0, new KeyValuePair<string, int>("horizon", horizonY));
                foreach (KeyValuePair<string, int> row in rows)
                {
                    double y = Math.Max(center.Y + 0.5, Math.Min(center.Y + center.Height - 0.5, row.Value));
                    eyeSeams.Add(new CaptureTool.EyeSeam($"seam1-eye-{row.Key}", center.RayThrough(center.X + 0.5, y)));
                    eyeSeams.Add(new CaptureTool.EyeSeam($"seam2-eye-{row.Key}", center.RayThrough(center.X + center.Width - 0.5, y)));
                }
            }

            string meta = CaptureTool.Meta(label, State, geometry, client, seams.ToArray(), horizonY, rig);
            return capture.Request(OutputDirectory, label, State, seams.ToArray(), horizonY, meta, views, eyeSeams);
        }

        string Status() =>
            $"{(rigActive ? "on" : "off")}: {rig.Describe()} | span {geometry.Span.Value} {(span.IsSpanned ? "applied" : "pending")} {span.Target} | " +
            $"window {(Win32.IsAlive(window) ? Win32.ClientRectOnScreen(window).ToString() : "not found")}, Unity {Screen.width}x{Screen.height} | " +
            $"curtains {curtains?.Created ?? 0} | grid {(grid.Visible ? "on" : "off")} | layout {(geometry.Measured.Value ? "measured" : "desktop-plane")}" +
            $" | screens {screensText?.TrimEnd('.')}{(screensOutOfOrder != null ? ", out of order" : "")}" +
            (BandReport() is string band ? $" | band {band}" : "") +
            (tuner.IsOpen ? $" | tuner open at {tuner.StepTitle}" : "") +
            $" | hud markers {(geometry.HudSideMarkers.Value ? $"on, {rig.SideAnswers} side answers so far" : "off")}" +
            $" | flight hud {bender.Status}" +
            $" | eye {eyeHold.Status}" +
            $" | straight ahead {straightAhead.Status}" +
            $" | menus {menus.Status}" +
            $" | hud corners {corners.Status}" +
            $" | vegetation cull {(geometry.VegetationAllScreens.Value ? $"all screens +{geometry.VegetationMarginDeg.Value:0.#}°, {rig.WidenedCulls} frames" : "game's")}";

        string State => rig.Built ? "rig-on" : "rig-off";

        // "Section.Key", where the section may itself contain dots (Geometry.Left.AngleDeg).
        ConfigEntryBase FindSetting(string[] args)
        {
            if (args.Length == 0) throw new ArgumentException("name a setting as Section.Key, e.g. Geometry.Left.AngleDeg");
            int dot = args[0].LastIndexOf('.');
            if (dot <= 0) throw new ArgumentException($"'{args[0]}' isn't Section.Key");
            var definition = new ConfigDefinition(args[0].Substring(0, dot), args[0].Substring(dot + 1));
            if (!Config.ContainsKey(definition)) throw new ArgumentException($"no setting {args[0]}");
            return Config[definition];
        }

        static string Describe(ConfigEntryBase entry) => $"{entry.Definition.Section}.{entry.Definition.Key} = {entry.GetSerializedValue()}";

        float GameFov()
        {
            if (rig.Built) return rig.GameFov;
            Camera main = Camera.main;
            return main != null ? main.fieldOfView : 50f;
        }

        string OutputDirectory =>
            string.IsNullOrEmpty(outputDir.Value) ? Path.Combine(Paths.BepInExRootPath, "nuclear-triad") : outputDir.Value;

        static string Label(string[] args) => args.Length > 0 ? args[0] : "manual";

        void OnDestroy()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            MainCameraScreenPointPatch.Hook = null;
            VegetationCullPatch.Hook = null;
            OffScreenArrowPatch.Hook = null;
            ObjectiveArrowPatch.Hook = null;
            curtains?.Dispose();
            curtains = null;
            // If the game destroys BepInEx's manager object, nothing here runs any more.
            if (!quitting)
                Logger.LogWarning(@"Plugin destroyed while the game runs; the control channel has stopped. Set [Chainloader] HideManagerGameObject = true in BepInEx\config\BepInEx.cfg.");
        }
    }
}
