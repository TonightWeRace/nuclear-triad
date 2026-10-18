using BepInEx.Logging;
using UnityEngine;

namespace NuclearTriad.Dev
{
    /// <summary>
    /// World-fixed great circles around the eye: the horizon (yellow), meridians every 15° (cyan, the
    /// north-south one white), and the horizon tilted ±30° about the world's north axis (green). A
    /// great circle projects to a straight line on any flat panel, so every line must run straight
    /// and unbroken across the seams, the bezel acting as a window frame. The world cameras draw it,
    /// so the cockpit still frames it.
    /// </summary>
    internal sealed class SkyGrid
    {
        readonly ManualLogSource log;
        GameObject root;
        string shaderName;

        public SkyGrid(ManualLogSource log)
        {
            this.log = log;
        }

        public bool Visible => root != null && root.activeSelf;

        public string Show(bool show)
        {
            if (show && root == null) Create();
            if (root != null) root.SetActive(show);
            return show ? $"sky grid on (shader {shaderName})" : "sky grid off";
        }

        /// <summary>Keeps the grid centered on the eye and fixed to the world's axes.</summary>
        public void Follow(Camera eye)
        {
            if (root == null || !root.activeSelf || eye == null) return;
            root.transform.SetPositionAndRotation(eye.transform.position, Quaternion.identity);
        }

        void Create()
        {
            // On the default layer, so the world cameras draw it and the cockpit frames it.
            root = LineDrawing.CreateObject("NuclearTriad SkyGrid", 0, out shaderName);
            var circles = new LineDrawing.Circles(1000f);
            circles.Add(a => new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)), new Color32(255, 220, 0, 255));
            for (int k = 0; k < 12; k++)
            {
                float azimuth = k * 15f * Mathf.Deg2Rad;
                circles.Add(a => new Vector3(Mathf.Cos(a) * Mathf.Sin(azimuth), Mathf.Sin(a), Mathf.Cos(a) * Mathf.Cos(azimuth)),
                    k == 0 ? new Color32(255, 255, 255, 255) : new Color32(0, 220, 255, 255));
            }
            foreach (float tiltDeg in new[] { 30f, -30f })
            {
                float tilt = tiltDeg * Mathf.Deg2Rad;
                circles.Add(a => new Vector3(Mathf.Sin(a) * Mathf.Cos(tilt), Mathf.Sin(a) * Mathf.Sin(tilt), Mathf.Cos(a)), new Color32(60, 255, 60, 255));
            }
            root.GetComponent<MeshFilter>().sharedMesh = circles.Build("NuclearTriad SkyGrid");
            log.LogInfo($"Sky grid built with shader {shaderName}");
        }
    }
}
