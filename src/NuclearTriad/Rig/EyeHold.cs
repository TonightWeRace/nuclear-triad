using System;
using System.Collections.Generic;
using BepInEx.Logging;
using NuclearTriad.Geometry;
using UnityEngine;

namespace NuclearTriad.Rig
{
    /// <summary>
    /// Holds the eye still in cockpit free look while the nose is on a screen (Rig.HoldEyeInFreeLook).
    /// Past about 33° of pan the game slides the eye sideways, like leaning, so on the rig's wings
    /// the near cockpit shifts under the flight HUD, which is aimed at infinity. Late in the frame,
    /// after the game has placed the camera, this replaces just that sideways part of the camera's
    /// local position with the held slide (FreeLook): none until the nose leaves the outermost
    /// screen, then the game's own ramp. The game sets the camera's position afresh every frame and
    /// never reads it back, so switching this or the rig off gives the game's lean on the next
    /// frame. Head trackers drive the eye themselves and are left alone.
    /// </summary>
    internal sealed class EyeHold
    {
        readonly ManualLogSource log;
        readonly TriadRig rig;
        readonly RigConfig config;
        bool failed;

        public EyeHold(ManualLogSource log, TriadRig rig, RigConfig config)
        {
            this.log = log;
            this.rig = rig;
            this.config = config;
        }

        /// <summary>What the hold did last frame, for status.</summary>
        public string Status { get; private set; } = "not seen yet";

        public void Late()
        {
            if (failed) return;
            try
            {
                Hold();
            }
            catch (Exception e)
            {
                failed = true;
                Status = "stopped after an error; see the log";
                log.LogError($"Eye hold stopped; free look uses the game's lean. {e}");
            }
        }

        void Hold()
        {
            if (!config.HoldEyeInFreeLook.Value)
            {
                Status = "the game's lean";
                return;
            }
            CameraStateManager manager = SceneSingleton<CameraStateManager>.i;
            if (!rig.Built || manager == null || manager.currentState != manager.cockpitState)
            {
                Status = "not in the cockpit with the rig on";
                return;
            }
            if (PlayerSettings.useTrackIR && PlayerSettings.headTrackerType != HeadTrackerType.None)
            {
                Status = "head tracker in charge";
                return;
            }
            IList<Panel> panels = rig.CurrentPanels;
            if (panels == null || panels.Count < 3) return;

            Transform eye = manager.transform;
            float pan = eye.localEulerAngles.y;
            if (pan > 180f) pan -= 360f;
            var (right, left) = FreeLook.Reach(panels);
            float slide = (float)FreeLook.HeldSlide(pan, right, left);
            Vector3 p = eye.localPosition;
            eye.localPosition = new Vector3(slide, p.y, p.z);
            Status = $"held: pan {pan:+0;-0}°, reach {right:0}° right / {left:0}° left, slide {slide * 100:0.#} cm " +
                     $"(the game's {FreeLook.GameSlide(pan) * 100:0.#} cm)";
        }
    }

    /// <summary>Runs its tick in Update before the game's scripts.</summary>
    [DefaultExecutionOrder(-10000)]
    internal sealed class EarlyDriver : MonoBehaviour
    {
        internal Action Tick;

        void Update() => Tick?.Invoke();
    }

    /// <summary>Runs its tick in LateUpdate after the game has moved the camera (its camera manager runs at order 2).</summary>
    [DefaultExecutionOrder(10000)]
    internal sealed class LateDriver : MonoBehaviour
    {
        internal Action Tick;

        void LateUpdate() => Tick?.Invoke();
    }
}
