using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using NuclearTriad.Geometry;
using UnityEngine;
using UnityEngine.UI;

namespace NuclearTriad.Rig
{
    /// <summary>
    /// The flight HUD on the wing screens. The game anchors its flight HUD (pitch ladder, heading
    /// tape, speed and altitude readouts) at the screen point of the aircraft's nose and draws it
    /// for the center screen, and it does that in Update, before it moves the camera in
    /// LateUpdate, so the HUD trails the view by a frame whenever the view turns.
    ///
    /// Late in the frame, after the camera has moved, this places the anchor, the HUD's roll and
    /// the velocity vector again with the camera as it will render. When the anchor is on a wing,
    /// it then turns the HUD's own parts about the anchor to the wing's local horizontal there,
    /// and sizes them per Hud.BendScale: by default to look as big from the real eye as on the
    /// center screen. Parts that other scripts place by screen point (boresight cues,
    /// radar-warning icons) are left alone; the main camera's patched projection already puts
    /// them right. Early in the next frame, before the game's scripts run, every bent part goes
    /// back exactly as it was, so the game always works from its own values.
    ///
    /// The game's floating origin moves every root object in the scene back toward zero, the HUD's
    /// canvas included, in the camera manager's LateUpdate, by a world shift in whole 64 m steps,
    /// and the canvas is back over the window before it draws. The game places its HUD in Update,
    /// before that, so the move carries it along harmlessly. This places late, after it, so it
    /// never writes a screen point as a world position: it works out each piece's place in the
    /// canvas from where the canvas always draws (PlaceOnScreen). Writing a screen point into a
    /// carried canvas put the HUD a world shift off for a frame, its depth no longer zero, and the
    /// game's helmet display, seeing the HUD gone, flashed up.
    ///
    /// The aircraft can change under the same HUD, at a respawn, so the cockpit is read from the
    /// game's HUD every frame; aiming from the previous aircraft put the HUD far off until it went.
    /// </summary>
    internal sealed class HudBender
    {
        // Components whose scripts place their objects by screen point.
        static readonly HashSet<string> ScreenPlaced = new HashSet<string>
        {
            "HUDBoresightState", "HUDBombingState", "HUDTurretCrosshair", "RadarWarning", "ThreatItem", "ObjectiveOverlay", "AirbaseOverlay",
        };

        struct Saved
        {
            public Transform Transform;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        static readonly FieldInfo CockpitField = AccessTools.Field(typeof(FlightHud), "cockpitTransform");
        static readonly FieldInfo CockpitBodyField = AccessTools.Field(typeof(FlightHud), "cockpitRB");

        readonly ManualLogSource log;
        readonly TriadRig rig;
        readonly RigConfig config;
        readonly List<Transform> parts = new List<Transform>();
        readonly List<Saved> saved = new List<Saved>();
        FlightHud hud;
        Transform center;
        Transform pitch;
        Transform cockpit;
        Rigidbody cockpitBody;
        Image velocityVector;
        Canvas canvas;
        int carried;
        float nextScan;
        bool failed;

        public HudBender(ManualLogSource log, TriadRig rig, RigConfig config)
        {
            this.log = log;
            this.rig = rig;
            this.config = config;
        }

        /// <summary>What the bend did last frame, for status.</summary>
        public string Status { get; private set; } = "not seen yet";

        /// <summary>Early in the frame: puts every part bent last frame back as it was.</summary>
        public void Early()
        {
            foreach (Saved s in saved)
            {
                if (s.Transform == null) continue;
                s.Transform.localPosition = s.Position;
                s.Transform.localRotation = s.Rotation;
                s.Transform.localScale = s.Scale;
            }
            saved.Clear();
        }

        /// <summary>Late in the frame, after the camera has moved: places the flight HUD again and bends it onto a wing.</summary>
        public void Late()
        {
            if (failed) return;
            try
            {
                Bend();
            }
            catch (Exception e)
            {
                failed = true;
                Early();
                Status = "stopped after an error; see the log";
                log.LogError($"Flight HUD bend stopped; the HUD is drawn as the game does. {e}");
            }
        }

        void Bend()
        {
            FlightHud current = SceneSingleton<FlightHud>.i;
            if (current != hud) Track(current);
            Camera main = rig.Main;
            if (center == null || !config.HudBendFlight.Value || !rig.Built || main == null)
            {
                Status = center == null ? "no flight HUD" : !config.HudBendFlight.Value ? "drawn as the game does (bend off)" : "rig off";
                return;
            }
            cockpit = CockpitField?.GetValue(hud) as Transform;
            cockpitBody = CockpitBodyField?.GetValue(hud) as Rigidbody;
            if (cockpit == null)
            {
                Status = "no aircraft";
                return;
            }
            if (Time.unscaledTime >= nextScan) Scan();
            WatchCanvas();
            // The anchor as a screen point, and as a position in the canvas as it sits this moment,
            // which the parts are bent about.
            Vector3 anchor = Place(main);
            Vector3 anchorWorld = center.position;
            Camera wing = rig.WingAt(anchor);
            if (wing == null)
            {
                Status = $"on the center screen, placed after the camera moves{Shifts()}";
                return;
            }

            Linear2 map = WingMap(main, wing, anchor);
            float drawn = center.eulerAngles.z;
            float turn = Mathf.DeltaAngle(drawn, (float)HudBend.Fit(map, drawn).RotationDeg);
            float size = Size(map, wing, anchor);
            Quaternion spin = Quaternion.Euler(0, 0, turn);
            foreach (Transform part in parts)
            {
                if (part == null || !part.gameObject.activeInHierarchy) continue;
                saved.Add(new Saved { Transform = part, Position = part.localPosition, Rotation = part.localRotation, Scale = part.localScale });
                Vector3 offset = part.position - anchorWorld;
                Vector3 bent = spin * new Vector3(offset.x, offset.y, 0) * size;
                part.position = new Vector3(anchorWorld.x + bent.x, anchorWorld.y + bent.y, part.position.z);
                part.rotation = spin * part.rotation;
                part.localScale *= size;
            }
            Status = $"bent on the {(wing == rig.Wing(false) ? "left" : "right")} screen: turned {turn:+0.0;-0.0}°, " +
                     $"size x{size:0.00} ({config.HudBendScale.Value}), {saved.Count} parts{Shifts()}";
        }

        string Shifts() => carried > 0 ? $"; placed through {carried} frames with the canvas carried off (origin shifts)" : "";

        void Track(FlightHud current)
        {
            Early();
            hud = current;
            parts.Clear();
            center = current != null ? current.GetHUDCenter() : null;
            canvas = CanvasPlacement.RootOf(center);
            pitch = (AccessTools.Field(typeof(FlightHud), "pitchCompassCenter")?.GetValue(current) as GameObject)?.transform;
            velocityVector = current != null ? current.velocityVector : null;
            nextScan = 0;
        }

        // The group's own parts: the largest pieces of its tree that hold nothing placed by screen
        // point. Rescanned once a second, as weapon cues come and go with the weapon selected.
        void Scan()
        {
            nextScan = Time.unscaledTime + 1f;
            parts.Clear();
            if (center == null) return;
            foreach (Transform child in center) Collect(child);
        }

        void Collect(Transform node)
        {
            if (IsScreenPlaced(node)) return;
            if (!HoldsScreenPlaced(node))
            {
                parts.Add(node);
                return;
            }
            foreach (Transform child in node) Collect(child);
        }

        static bool IsScreenPlaced(Transform node)
        {
            foreach (Component c in node.GetComponents<Component>())
                if (c != null && ScreenPlaced.Contains(c.GetType().Name)) return true;
            return false;
        }

        static bool HoldsScreenPlaced(Transform node)
        {
            foreach (Transform child in node)
                if (IsScreenPlaced(child) || HoldsScreenPlaced(child)) return true;
            return false;
        }

        // What the game's flight HUD does in Update, again, with the camera as it will render.
        // Returns the anchor's screen point.
        Vector3 Place(Camera main)
        {
            CameraStateManager manager = SceneSingleton<CameraStateManager>.i;
            Vector3 look = manager != null ? manager.transform.forward : main.transform.forward;
            Vector3 from = manager != null ? manager.transform.position : main.transform.position;

            Vector3 nose = cockpit.position + cockpit.forward * 4000f;
            Vector3 at = main.WorldToScreenPoint(nose);
            if (Vector3.Dot(look, nose - from) > 0f) at.z = 0f;
            PlaceOnScreen(center, at);
            center.eulerAngles = new Vector3(0, 0, -(main.transform.eulerAngles.z - cockpit.eulerAngles.z));
            if (pitch != null) pitch.eulerAngles = new Vector3(0, 0, -main.transform.eulerAngles.z);

            if (velocityVector != null && velocityVector.gameObject.activeSelf && cockpitBody != null)
            {
                Vector3 ahead = cockpit.position + cockpitBody.velocity * 1000f;
                Vector3 vv = main.WorldToScreenPoint(ahead);
                if (Vector3.Dot(look, cockpitBody.velocity) > 0f) vv.z = 0f;
                PlaceOnScreen(velocityVector.transform, vv);
            }
            return at;
        }

        // Puts a piece of the HUD at a screen point in its canvas as the canvas draws, never where
        // its transform sits this moment (CanvasPlacement).
        void PlaceOnScreen(Transform piece, Vector3 screen) => CanvasPlacement.At(canvas, piece, screen);

        // Counts the frames where the canvas sits away from the window (an origin shift carrying it),
        // and logs the first few, as the evidence that placing through them is needed.
        void WatchCanvas()
        {
            if (!CanvasPlacement.Usable(canvas)) return;
            Vector3 off = canvas.transform.position - CanvasPlacement.Origin(canvas);
            if (off.sqrMagnitude <= 1f) return;
            carried++;
            if (carried <= 10)
                log.LogInfo($"[hud-trace] frame {Time.frameCount}: the HUD's canvas sits {off.ToString("F0")} away from the window this " +
                            $"LateUpdate (an origin shift carrying it); placing through it ({carried} so far)");
        }

        float Size(Linear2 map, Camera wing, Vector3 anchor)
        {
            switch (config.HudBendScale.Value)
            {
                case HudScale.None:
                    return 1f;
                case HudScale.Rendered:
                    return (float)Math.Sqrt(Math.Abs(map.Determinant));
                default:
                    return (float)(rig.PhysicalSizeAt(wing, anchor) ?? 1.0);
            }
        }

        // The wing camera's pixels per radian of view at the anchor, against the main camera's at
        // its own center: how the wing's rendering differs there from what the game draws for.
        static Linear2 WingMap(Camera main, Camera wing, Vector3 anchor)
        {
            Vector3 eye = main.transform.position;
            Vector3 there = wing.ScreenPointToRay(anchor).direction;
            Linear2 onWing = PixelsPerRadian(wing, eye, there, main.transform);
            Linear2 atCenter = PixelsPerRadian(main, eye, main.transform.forward, main.transform);
            return onWing * atCenter.Inverse();
        }

        // Screen pixels moved per radian, for small turns of direction d along the main camera's
        // own right and up axes.
        static Linear2 PixelsPerRadian(Camera camera, Vector3 eye, Vector3 d, Transform frame)
        {
            const float step = 0.002f, range = 1000f;
            Vector3 right = Vector3.ProjectOnPlane(frame.right, d).normalized;
            Vector3 up = Vector3.ProjectOnPlane(frame.up, d).normalized;
            Vector3 p0 = Project(camera, eye + d * range);
            Vector3 px = Project(camera, eye + (d + right * step).normalized * range);
            Vector3 py = Project(camera, eye + (d + up * step).normalized * range);
            return Linear2.FromColumns((px.x - p0.x) / step, (px.y - p0.y) / step, (py.x - p0.x) / step, (py.y - p0.y) / step);
        }

        // The two-argument overload isn't patched: each camera's own answer.
        static Vector3 Project(Camera camera, Vector3 world) => camera.WorldToScreenPoint(world, Camera.MonoOrStereoscopicEye.Mono);
    }
}
