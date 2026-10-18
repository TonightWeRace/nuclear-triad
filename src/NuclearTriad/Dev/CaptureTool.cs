using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BepInEx.Logging;
using NuclearTriad.Geometry;
using NuclearTriad.Rig;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace NuclearTriad.Dev
{
    /// <summary>
    /// capture: the whole window as a PNG at the end of the frame, 512 px crops on each seam at the
    /// horizon and mid-frame, and a JSON sidecar describing every camera, for the eye-space checks.
    /// Encoding and writing run on a worker thread; results are logged from the main thread.
    /// </summary>
    internal sealed class CaptureTool
    {
        const int CropSize = 512;
        const double EyeViewFovDeg = 30;
        static readonly Color32 Gap = new Color32(24, 24, 24, 255);

        /// <summary>An eye-space view to render: a name for the file and the eye-space direction it centers on.</summary>
        internal sealed class EyeSeam
        {
            public EyeSeam(string name, Vec3 direction)
            {
                Name = name;
                Direction = direction;
            }

            public string Name { get; }
            public Vec3 Direction { get; }
        }

        readonly MonoBehaviour host;
        readonly ManualLogSource log;
        readonly ConcurrentQueue<string> results = new ConcurrentQueue<string>();

        public CaptureTool(MonoBehaviour host, ManualLogSource log)
        {
            this.host = host;
            this.log = log;
        }

        /// <summary>
        /// Schedules a capture of this frame. Seams and horizon are in window pixels, origin bottom
        /// left. With the rig on, views and eyeSeams add eye-space views of the seams, in which
        /// great circles are straight lines across the gap.
        /// </summary>
        public string Request(string outputDir, string label, string state, int[] seams, int horizonY, string meta,
            IList<WindowView> views = null, IList<EyeSeam> eyeSeams = null)
        {
            string dir = Path.Combine(outputDir, "captures");
            Directory.CreateDirectory(dir);
            // Milliseconds in the name, so two captures in the same second don't overwrite each other.
            string stem = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Safe(label)}-{state}-b{VersionGuard.SteamBuild ?? "unknown"}");
            host.StartCoroutine(Grab(stem, seams, horizonY, meta, views, eyeSeams));
            return $"capture -> {stem}.png";
        }

        /// <summary>Logs finished captures; call from Update.</summary>
        public void Drain()
        {
            while (results.TryDequeue(out string message))
            {
                if (message.StartsWith("ERROR ")) log.LogError(message.Substring(6));
                else log.LogInfo(message);
            }
        }

        IEnumerator Grab(string stem, int[] seams, int horizonY, string meta, IList<WindowView> views, IList<EyeSeam> eyeSeams)
        {
            yield return new WaitForEndOfFrame();
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            int width = shot.width, height = shot.height;
            Color32[] pixels = shot.GetPixels32();
            UnityEngine.Object.Destroy(shot);
            ThreadPool.QueueUserWorkItem(_ => Write(stem, pixels, width, height, seams, horizonY, meta, views, eyeSeams));
        }

        void Write(string stem, Color32[] pixels, int width, int height, int[] seams, int horizonY, string meta,
            IList<WindowView> views, IList<EyeSeam> eyeSeams)
        {
            try
            {
                File.WriteAllBytes(stem + ".png", Png(pixels, width, height));
                var rows = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>("mid", height / 2) };
                if (horizonY >= 0 && horizonY < height) rows.Insert(0, new KeyValuePair<string, int>("horizon", horizonY));
                int crops = 0;
                for (int s = 0; s < seams.Length; s++)
                {
                    foreach (KeyValuePair<string, int> row in rows)
                    {
                        File.WriteAllBytes($"{stem}-seam{s + 1}-{row.Key}.png", Crop(pixels, width, height, seams[s], row.Value));
                        crops++;
                    }
                }
                int eyeViews = 0;
                if (views != null && eyeSeams != null)
                {
                    foreach (EyeSeam seam in eyeSeams)
                    {
                        var eye = new EyeView(seam.Direction, CropSize, EyeViewFovDeg);
                        int[] map = eye.SourceIndices(views, width, height);
                        var view = new Color32[map.Length];
                        for (int k = 0; k < map.Length; k++) view[k] = map[k] >= 0 ? pixels[map[k]] : Gap;
                        File.WriteAllBytes($"{stem}-{seam.Name}.png", Png(view, CropSize, CropSize));
                        eyeViews++;
                    }
                }
                File.WriteAllText(stem + ".json", meta);
                results.Enqueue($"Capture written: {stem}.png ({width}x{height}), {crops} seam crops, {eyeViews} eye-space seam views, sidecar .json");
            }
            catch (Exception e)
            {
                results.Enqueue($"ERROR Capture failed: {e}");
            }
        }

        // Pixels run in rows from the bottom up, as Unity's textures do.
        static byte[] Crop(Color32[] pixels, int width, int height, int centerX, int centerY)
        {
            int w = Math.Min(CropSize, width), h = Math.Min(CropSize, height);
            int x0 = Mathf.Clamp(centerX - w / 2, 0, width - w);
            int y0 = Mathf.Clamp(centerY - h / 2, 0, height - h);
            var crop = new Color32[w * h];
            for (int y = 0; y < h; y++) Array.Copy(pixels, (y0 + y) * width + x0, crop, y * w, w);
            return Png(crop, w, h);
        }

        static byte[] Png(Color32[] pixels, int width, int height) =>
            ImageConversion.EncodeArrayToPNG(pixels, GraphicsFormat.R8G8B8A8_SRGB, (uint)width, (uint)height);

        static string Safe(string label) =>
            new string(label.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());

        /// <summary>The sidecar: window, layout and every rig camera's rect, frustum, orientation and matrix.</summary>
        public static string Meta(string label, string state, RigConfig config, PixelRect? client, int[] seams, int horizonY, TriadRig rig)
        {
            var j = new StringBuilder();
            j.Append("{\n");
            j.Append($"  \"label\": {Str(label)},\n  \"time\": {Str(DateTime.Now.ToString("o"))},\n  \"state\": {Str(state)},\n");
            j.Append($"  \"game\": {Str(Application.version)},\n  \"unity\": {Str(Application.unityVersion)},\n");
            j.Append($"  \"steamBuild\": {Str(VersionGuard.SteamBuild ?? "unknown")},\n  \"plugin\": {Str(Plugin.Version)},\n");
            j.Append($"  \"layout\": {Str(config.Measured.Value ? "measured" : "desktop-plane")},\n  \"span\": {Str(config.Span.Value.ToString())},\n");
            j.Append($"  \"window\": [{Screen.width}, {Screen.height}],\n");
            if (client.HasValue) j.Append($"  \"client\": [{client.Value.Left}, {client.Value.Top}, {client.Value.Right}, {client.Value.Bottom}],\n");
            j.Append($"  \"seams\": [{string.Join(", ", seams)}],\n  \"horizonY\": {horizonY},\n");
            j.Append("  \"views\": [");
            bool first = true;
            foreach (TriadRig.ViewState v in rig.Views)
            {
                if (v.Camera == null) continue;
                Camera c = v.Camera;
                j.Append(first ? "\n" : ",\n");
                first = false;
                j.Append($"    {{ \"name\": {Str(v.Name)}, \"camera\": {Str(c.name)},\n");
                j.Append($"      \"rect\": [{Num(c.rect.x)}, {Num(c.rect.y)}, {Num(c.rect.width)}, {Num(c.rect.height)}], \"pixelRect\": [{Num(c.pixelRect.x)}, {Num(c.pixelRect.y)}, {Num(c.pixelRect.width)}, {Num(c.pixelRect.height)}],\n");
                j.Append($"      \"frustum\": {{ \"left\": {Num(v.Frustum.Left)}, \"right\": {Num(v.Frustum.Right)}, \"bottom\": {Num(v.Frustum.Bottom)}, \"top\": {Num(v.Frustum.Top)}, \"distanceMm\": {Num(v.Frustum.DistanceMm)} }},\n");
                j.Append($"      \"panel\": {{ \"lowerLeft\": {Vec(v.Panel.LowerLeft)}, \"lowerRight\": {Vec(v.Panel.LowerRight)}, \"upperLeft\": {Vec(v.Panel.UpperLeft)} }},\n");
                j.Append($"      \"coverage\": [{Num(v.Viewport.U0)}, {Num(v.Viewport.V0)}, {Num(v.Viewport.U1)}, {Num(v.Viewport.V1)}],\n");
                j.Append($"      \"near\": {Num(c.nearClipPlane)}, \"far\": {Num(c.farClipPlane)}, \"fieldOfView\": {Num(c.fieldOfView)},\n");
                Quaternion r = c.transform.rotation;
                j.Append($"      \"worldRotation\": [{Num(r.x)}, {Num(r.y)}, {Num(r.z)}, {Num(r.w)}],\n");
                Matrix4x4 p = c.projectionMatrix;
                j.Append($"      \"projection\": [{string.Join(", ", Enumerable.Range(0, 16).Select(i => Num(p[i / 4, i % 4])))}] }}");
            }
            j.Append(first ? "]\n}\n" : "\n  ]\n}\n");
            return j.ToString();
        }

        static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        static string Vec(Vec3 v) => $"[{Num(v.X)}, {Num(v.Y)}, {Num(v.Z)}]";

        static string Str(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
