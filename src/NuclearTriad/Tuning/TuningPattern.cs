using System;
using System.Collections.Generic;
using BepInEx.Logging;
using NuclearTriad.Dev;
using NuclearTriad.Geometry;
using UnityEngine;

namespace NuclearTriad.Tuning
{
    /// <summary>
    /// The tuner's lines. They are fixed to the view rather than the world, so they hold still
    /// whatever the aircraft does: a level line at eye height (yellow), a yellow ring straight
    /// ahead where it crosses the center screen, and two lines through each bezel gap at eye
    /// height, rising (cyan) and falling (green) at 30°. Every line must run straight through the
    /// gaps once the geometry matches the rig. They sit on the cockpit's layer, 2 m from the eye,
    /// so the cockpit cameras draw them over the cockpit (whose canopy frames would otherwise hide
    /// them near the seams). Being great circles, the lines project the same at any distance.
    /// </summary>
    internal sealed class TuningPattern
    {
        const double SlopeDeg = 30;
        const double RingDeg = 2;
        // Inside the cockpit cameras' clip range (0.01 to 5 m in this build).
        const float Radius = 2f;
        static readonly Color32 Yellow = new Color32(255, 220, 0, 255);

        readonly ManualLogSource log;
        GameObject root;
        MeshFilter filter;
        List<Vec3> seams;

        public TuningPattern(ManualLogSource log)
        {
            this.log = log;
        }

        public bool Visible => root != null && root.activeSelf;

        public void Show(bool show)
        {
            if (show && root == null)
            {
                int cockpit = LayerMask.NameToLayer("Cockpit");
                if (cockpit < 0) log.LogWarning("No Cockpit layer in this build, so the tuning lines draw behind the cockpit");
                root = LineDrawing.CreateObject("NuclearTriad TuningPattern", Math.Max(cockpit, 0), out string shaderName);
                filter = root.GetComponent<MeshFilter>();
                log.LogInfo($"Tuning lines built with shader {shaderName}");
            }
            if (root != null) root.SetActive(show);
        }

        /// <summary>Keeps the lines on the eye and turned with it, and redraws them when the gaps move.</summary>
        public void Follow(Camera eye, IList<Panel> panels)
        {
            if (!Visible || eye == null) return;
            root.transform.SetPositionAndRotation(eye.transform.position, eye.transform.rotation);

            List<Vec3> now = panels != null && panels.Count > 1 ? TuningLines.SeamDirections(panels) : new List<Vec3>();
            if (filter.sharedMesh != null && seams != null && Same(now, seams)) return;
            Mesh old = filter.sharedMesh;
            filter.sharedMesh = Build(now);
            if (old != null) UnityEngine.Object.Destroy(old);
            seams = now;
        }

        static Mesh Build(List<Vec3> seams)
        {
            var circles = new LineDrawing.Circles(Radius);
            Add(circles, TuningLines.EyeLevel, Yellow);
            circles.Add(a => ToVector(TuningLines.AroundStraightAhead(RingDeg, a)), Yellow, 90);
            foreach (Vec3 seam in seams)
            {
                Add(circles, TuningLines.Through(seam, SlopeDeg), new Color32(0, 220, 255, 255));
                Add(circles, TuningLines.Through(seam, -SlopeDeg), new Color32(60, 255, 60, 255));
            }
            return circles.Build("NuclearTriad TuningPattern");
        }

        static void Add(LineDrawing.Circles circles, GreatCircle circle, Color32 color) =>
            circles.Add(a => ToVector(circle.At(a)), color, 360);

        static Vector3 ToVector(Vec3 p) => new Vector3((float)p.X, (float)p.Y, (float)p.Z);

        static bool Same(List<Vec3> a, List<Vec3> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if ((a[i] - b[i]).Length > 1e-6) return false;
            return true;
        }
    }
}
