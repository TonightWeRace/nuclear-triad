using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using NuclearTriad.Geometry;
using UnityEngine;
using UnityEngine.UI;

namespace NuclearTriad.Rig
{
    /// <summary>
    /// Some of the game's HUD pieces take the window's middle to be straight ahead. With the rig,
    /// straight ahead is wherever the center screen draws it, and the eye height moves it.
    /// - The target designator: the diamond at the view's center. Targeting takes the markers
    ///   nearest it on screen, and it fades as the flight HUD's anchor comes onto it, so with the
    ///   view on the boresight it sits on the nose and hides.
    /// - The arrows that pin off-screen markers to the window's edge: the shared pin, and the
    ///   objective markers' own copy of it.
    ///
    /// While Hud.FollowStraightAhead is on, the designator is put at straight ahead every frame,
    /// its place in the canvas worked out from the screen point (CanvasPlacement), never from where
    /// it already sits: moving it relative to its current place stacked a second move onto copies
    /// of an already moved HUD at a respawn. When following stops, it goes back to the place the
    /// game first gave it. The arrows are measured from straight ahead too
    /// (Patches/OffScreenArrowPatch.cs and ObjectiveArrowPatch.cs). Everything else stays where the
    /// game lays it out: the helmet display, which hides as the flight HUD's anchor comes near and
    /// works either way, and the airbase capture panel of cargo and troop weapons, which the game
    /// puts near the top of the window, where moving it up by the eye height pushed it off.
    /// Where straight ahead sits at the window's middle, as with the eye height at 0 on a rig
    /// whose side screens match, nothing changes.
    /// </summary>
    internal sealed class StraightAhead
    {
        // Within half a pixel of the window's middle, the game's placement stands.
        const float Negligible = 0.5f;

        readonly ManualLogSource log;
        readonly TriadRig rig;
        readonly RigConfig config;
        Vector2 point;
        bool active;
        bool failed;

        // The designator this follows, the canvas it draws on, and the place the game first gave
        // it, in its parent, before anything here moved one.
        Transform designator;
        Canvas designatorCanvas;
        Vector3? designatorHome;
        bool designatorPlaced;

        public StraightAhead(ManualLogSource log, TriadRig rig, RigConfig config)
        {
            this.log = log;
            this.rig = rig;
            this.config = config;
        }

        /// <summary>Where straight ahead sits against the window's middle, for status.</summary>
        public string Status { get; private set; } = "not seen yet";

        /// <summary>Late in the frame, after the camera has moved: finds straight ahead and puts the designator there.</summary>
        public void Late()
        {
            if (failed) return;
            try
            {
                Follow();
            }
            catch (Exception e)
            {
                failed = true;
                active = false;
                SendDesignatorHome();
                Status = "stopped after an error; see the log";
                log.LogError($"Following straight ahead stopped; the HUD pieces that assume the window's middle are placed as the game does. {e}");
            }
        }

        void Follow()
        {
            active = false;
            Camera main = rig.Main;
            if (!config.HudFollowStraightAhead.Value || !rig.Built || main == null)
            {
                Status = !config.HudFollowStraightAhead.Value ? "off" : "rig off";
                SendDesignatorHome();
                return;
            }

            // The two-argument overload isn't patched: the center camera's own answer.
            Vector3 ahead = main.WorldToScreenPoint(main.transform.position + main.transform.forward * 1000f, Camera.MonoOrStereoscopicEye.Mono);
            point = new Vector2(ahead.x, ahead.y);
            Vector2 offset = point - new Vector2(Screen.width / 2f, Screen.height / 2f);
            if (offset.magnitude <= Negligible)
            {
                Status = "at the window's middle, so the game's placement stands";
                SendDesignatorHome();
                return;
            }
            active = true;

            CombatHUD combat = SceneSingleton<CombatHUD>.i;
            PlaceDesignator(combat != null && combat.targetDesignator != null ? combat.targetDesignator.transform : null);
            Status = $"{offset.x:+0;-0;0}, {offset.y:+0;-0;0} px from the window's middle; the target designator and arrows follow it";
        }

        // Puts the designator at straight ahead, from the screen point alone.
        void PlaceDesignator(Transform piece)
        {
            if (piece == null) return;
            if (piece != designator)
            {
                designator = piece;
                designatorCanvas = CanvasPlacement.RootOf(piece);
            }
            if (designatorHome == null) designatorHome = piece.localPosition;
            CanvasPlacement.At(designatorCanvas, piece, new Vector3(point.x, point.y, 0f));
            designatorPlaced = true;
        }

        // Back to the place the game first gave it, once, when following stops.
        void SendDesignatorHome()
        {
            if (!designatorPlaced) return;
            designatorPlaced = false;
            if (designator != null && designatorHome.HasValue) designator.localPosition = designatorHome.Value;
        }

        /// <summary>The hook for the off-screen arrows: re-pins a pinned marker from straight ahead.</summary>
        public void Pin(Vector3 world, bool pinned, ref Vector3 rayToScreen, ref float arrowAngle)
        {
            if (!active || !pinned) return;
            CameraStateManager manager = SceneSingleton<CameraStateManager>.i;
            Camera main = manager != null ? manager.mainCamera : null;
            if (main == null) return;
            // The same call the game made, so the same answer, side screens included.
            Vector3 p = main.WorldToScreenPoint(world);
            (double x, double y, double angle) = EdgePin.Pin(p.x, p.y, point.x, point.y, Screen.width, Screen.height);
            rayToScreen = new Vector3((float)x, (float)y, 0f);
            arrowAngle = (float)angle;
        }

        // The objective marker's parts, which the game keeps private.
        static readonly FieldInfo ObjectivePointer = AccessTools.Field(typeof(ObjectiveOverlay), "objectivePointer");
        static readonly FieldInfo ObjectiveDot = AccessTools.Field(typeof(ObjectiveOverlay), "objectiveDot");
        static readonly FieldInfo SizeIndicator = AccessTools.Field(typeof(ObjectiveOverlay), "sizeIndicator");
        static readonly FieldInfo PointerTail = AccessTools.Field(typeof(ObjectiveOverlay), "pointerTail");
        bool objectiveMissing;

        /// <summary>
        /// The hook for objective and waypoint markers, just after the game placed one: places it
        /// and points its arrow again from straight ahead. The game turns a point behind the eye
        /// back round through the middle it measures from, and pins to the edge anything off the
        /// window or more than 90° off the view, so this does the same about straight ahead.
        /// </summary>
        public void PlaceObjective(ObjectiveOverlay overlay, Vector3 world, Vector3 direction)
        {
            if (!active || objectiveMissing || overlay == null) return;
            var pointer = ObjectivePointer?.GetValue(overlay) as Image;
            var dot = ObjectiveDot?.GetValue(overlay) as Image;
            var size = SizeIndicator?.GetValue(overlay) as Image;
            var tail = PointerTail?.GetValue(overlay) as Transform;
            if (pointer == null || dot == null || size == null || tail == null)
            {
                objectiveMissing = true;
                log.LogWarning("Objective markers aren't laid out as this build expects, so they keep to the window's middle");
                return;
            }
            CameraStateManager manager = SceneSingleton<CameraStateManager>.i;
            Camera main = manager != null ? manager.mainCamera : null;
            if (main == null) return;

            Vector3 p = main.WorldToScreenPoint(world);
            Vector2 toward = new Vector2(p.x, p.y) - point;
            if (p.z < 0f) toward = -toward;
            Vector2 at = point + toward;
            bool off = Vector3.Angle(manager.transform.forward, direction) > 90f;
            if (off || at.x < 0f || at.x > Screen.width || at.y < 0f || at.y > Screen.height)
            {
                (double x, double y, _) = EdgePin.Pin(at.x, at.y, point.x, point.y, Screen.width, Screen.height);
                at = new Vector2((float)x, (float)y);
            }

            var place = new Vector3(at.x, at.y, 0f);
            Vector3 moved = place - dot.transform.position;
            pointer.transform.position = place;
            dot.transform.position = place;
            size.transform.position = place;
            pointer.transform.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(toward.y, toward.x) * Mathf.Rad2Deg - 90f);
            // Its label follows: from the arrow's tail, or by the same move under the dot.
            TextNoOverlap label = overlay.TextNoOverlap;
            if (label != null) label.SetTarget(pointer.enabled ? (Vector2)tail.position : label.TargetPosition + (Vector2)moved);
        }
    }
}
